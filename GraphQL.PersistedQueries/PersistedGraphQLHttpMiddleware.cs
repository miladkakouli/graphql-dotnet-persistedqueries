using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GraphQL.Http;
using GraphQL.Server.Transports.AspNetCore.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GraphQL.PersistedQueries
{
    public class PersistedGraphQLHttpMiddleware
    {
        private readonly RequestDelegate _next;
        private const string JsonContentType = "application/json";
        private const string GraphQLContentType = "application/graphql";
        private const string FormUrlEncodedContentType = "application/x-www-form-urlencoded";
        private const string MultipartFormData = "multipart/form-data";
        private readonly PathString _path;

        public PersistedGraphQLHttpMiddleware(RequestDelegate next,string path)
        {
            _next = next;
            _path = new PathString(path);
        }

        public async Task InvokeAsync(HttpContext context, IDistributedCache cacheProvider, IDocumentWriter writer)
        {
            if (!IsGraphQLRequest(context))
            {
                await _next.Invoke(context);
                return;
            }

            var httpRequest = context.Request;
            try
            {
                //var graphqlRequest = new GraphQLPersistedRequest();
                if (HttpMethods.IsGet(httpRequest.Method) || (HttpMethods.IsPost(httpRequest.Method) && httpRequest.Query.ContainsKey(GraphQLRequest.QueryKey)))
                {
                    httpRequest.QueryString = new QueryString("?query=" + ExtractGraphQLRequestFromQueryString(httpRequest.Query, cacheProvider));
                    await _next.Invoke(context);
                    return;
                }
                else if (HttpMethods.IsPost(httpRequest.Method))
                {
                    if (!MediaTypeHeaderValue.TryParse(httpRequest.ContentType, out var mediaTypeHeader))
                    {
                        await _next.Invoke(context);
                        return;
                    }
                    var stream = httpRequest.Body;

                    var originalContent = await new StreamReader(stream, Encoding.UTF8, true, 1024, true).ReadToEndAsync();
                    ByteArrayContent requestContent = new StringContent(originalContent);

                    httpRequest.Body.Seek(0, SeekOrigin.Begin);

                    switch (mediaTypeHeader.MediaType.Value)
                    {
                        case JsonContentType:
                            var graphqlRequest = Deserialize<GraphQLPersistedRequest>(originalContent);
                            graphqlRequest._query = IsMD5(graphqlRequest.Query) ? Encoding.UTF8.GetString(cacheProvider.Get(graphqlRequest.Query)) : graphqlRequest.Query;
                            var json = JsonConvert.SerializeObject(graphqlRequest);
                            requestContent = new StringContent(json, Encoding.UTF8, JsonContentType);
                            break;
                        case GraphQLContentType:
                            var _query = IsMD5(originalContent) ? Encoding.UTF8.GetString(cacheProvider.Get(originalContent)) : originalContent;
                            requestContent = new StringContent(_query, Encoding.UTF8, GraphQLContentType);
                            break;
                        case FormUrlEncodedContentType:
                            var formCollection = originalContent.Split('&').Select(x =>
                                new KeyValuePair<string, string>(x.Split('=')[0], x.Remove(0, x.IndexOf('=') + 1)))
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                            ExtractGraphQLRequestFromPostBody(formCollection, cacheProvider);
                            requestContent = new FormUrlEncodedContent(formCollection);
                            break;
                        case MultipartFormData:
                            var boundary = MultipartRequestHelper.GetBoundary(MediaTypeHeaderValue.Parse(httpRequest.ContentType), 64);
                            var reader = new MultipartReader(boundary, httpRequest.Body);
                            var graphqlRequest2 = await DeserializeFormDataAsync(reader);
                            graphqlRequest2._query = IsMD5(graphqlRequest2.Query) ? Encoding.UTF8.GetString(cacheProvider.Get(graphqlRequest2.Query)) : graphqlRequest2.Query;
                            var json2 = JsonConvert.SerializeObject(graphqlRequest2);
                            requestContent = new StringContent(json2, Encoding.UTF8, JsonContentType);
                            break;
                    }
                    context.Request.Body = await requestContent.ReadAsStreamAsync();
                    context.Request.Body.Seek(0, SeekOrigin.Begin);
                    await _next.Invoke(context);
                }
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                await writer.WriteAsync(context.Response.Body, new ExecutionResult
                {
                    Errors = new ExecutionErrors
                    {
                        new ExecutionError(ex.Message, ex)
                    }
                });
                throw;
            }
        }

        private bool IsGraphQLRequest(HttpContext context)
        {
            return context.WebSockets.IsWebSocketRequest || context.Request.Path.StartsWithSegments(_path);
        }

        private static async Task<GraphQLPersistedRequest> DeserializeFormDataAsync(MultipartReader reader)
        {
            var gqlRequest = new GraphQLPersistedRequest();
            MultipartSection section = null;
            JArray objVaribales = new JArray();
            while ((section = await reader.ReadNextSectionAsync()) != null)
            {
                var contentDisposition = section.GetContentDispositionHeader();
                switch (contentDisposition.Name.Value)
                {
                    case "Query":
                        gqlRequest._query = await section.ReadAsStringAsync();
                        break;
                    case "Variables":
                        //if (gqlRequest.Variables == null) gqlRequest.Variables = new JObject();
                        gqlRequest.Variables = (JObject.Parse(await section.ReadAsStringAsync()));
                        break;
                    case "OperationName":
                        gqlRequest.OperationName = await section.ReadAsStringAsync();
                        break;
                    case "documentId":
                        gqlRequest.DocumentId = await section.ReadAsStringAsync();
                        break;
                    case "file":
                        //if(gqlRequest.Variables == null) gqlRequest.Variables = new JObject();
                        MemoryStream stream = new MemoryStream();
                        await section.Body.CopyToAsync(stream);

                        objVaribales.Add(new JObject(
                            new JProperty("fileName", contentDisposition.FileName.Value),
                            new JProperty("file", Convert.ToBase64String(stream.ToArray())
                            )));
                        break;
                }
            }

            if (gqlRequest.Variables.ContainsKey("files"))
                gqlRequest.Variables["files"] = objVaribales;
            else
                gqlRequest.Variables.Add("files", objVaribales);

            return gqlRequest;
        }

        public static T Deserialize<T>(string s)
        {
            return JsonConvert.DeserializeObject<T>(s);
        }

        private string ExtractGraphQLRequestFromQueryString(IQueryCollection qs, IDistributedCache cacheProvider)
        {
            return qs.TryGetValue(GraphQLPersistedRequest.QueryKey, out var queryValues) ? queryValues[0] : qs.TryGetValue(GraphQLPersistedRequest.DocumentIdKey, out var documentIdValues) ? 
                Encoding.UTF8.GetString(cacheProvider.Get(documentIdValues)) : null;
            //qs.
        }

        public static bool IsMD5(string input)
        {
            if (String.IsNullOrEmpty(input))
            {
                return false;
            }

            return Regex.IsMatch(input, "^[0-9a-fA-F]{32}$", RegexOptions.Compiled);
        }

        private void ExtractGraphQLRequestFromPostBody(IDictionary<string, string> fc, IDistributedCache cacheProvider)
        {
            fc[GraphQLRequest.QueryKey] = fc.TryGetValue(GraphQLRequest.QueryKey, out var queryValues) ? queryValues : fc.TryGetValue(GraphQLPersistedRequest.DocumentIdKey, out var documentIdValues) ? 
                Encoding.UTF8.GetString(cacheProvider.Get(documentIdValues)) : null;
        }

    }
}
