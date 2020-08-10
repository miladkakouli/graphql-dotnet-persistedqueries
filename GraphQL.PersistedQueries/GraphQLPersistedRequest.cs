using System;
using System.Collections.Generic;
using System.Text;
using GraphQL.Server.Transports.AspNetCore.Common;
using Newtonsoft.Json;

namespace GraphQL.PersistedQueries
{
    public class GraphQLPersistedRequest : GraphQLRequest
    {
        public const string DocumentIdKey = "documentId";

        [JsonProperty(QueryKey)]
        public string _query { get; set; }
        [JsonProperty(DocumentIdKey)]
        public string DocumentId { get; set; }
        public new string Query
        {
            get => !string.IsNullOrWhiteSpace(_query) ? _query : DocumentId;
        }
    }
}
