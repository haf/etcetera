﻿namespace etcetera
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using RestSharp;

    public class EtcdClient
    {
        readonly IRestClient _client;
        readonly Uri _root;
        readonly Uri _keysRoot;

        public EtcdClient(Uri etcdLocation)
        {
            var uriBuilder = new UriBuilder(etcdLocation)
            {
               Path = ""
            };
            _root = uriBuilder.Uri;
            _keysRoot = _root.AppendPath("v2").AppendPath("keys");
            _client = new RestClient(_root.ToString());
        }

        /// <summary>
        /// You can create hidden keys by prefixing with '_'
        /// </summary>
        /// <param name="key"></param>
        /// <param name="ttl">Time to live in seconds</param>
        /// <param name="value"></param>
        /// <returns></returns>
        public EtcdResponse Set(string key, object value, int ttl = 0)
        {
            return makeRequest(key, Method.PUT, req =>
            {
                req.AddParameter("value", value);
                if (ttl > 0)
                {
                    req.AddParameter("ttl", ttl);
                }
            });
        }

        public EtcdResponse CreateDir(string key, int ttl = 0)
        {
            return makeRequest(key, Method.PUT, req =>
            {
                req.AddParameter("dir", "true");
                if (ttl > 0)
                {
                    req.AddParameter("ttl", ttl);
                }
            });
        }

        public EtcdResponse Get(string key, bool sorted = false)
        {
            return makeRequest(key, Method.GET, req =>
            {
                //needed due to issue 469 - https://github.com/coreos/etcd/issues/469
                req.OnBeforeDeserialization = resp => { resp.ContentType = "application/json"; };

                if (sorted)
                {
                    req.AddParameter("sorted", true);
                }
            });
        }

        public EtcdResponse Queue(string key, object value)
        {
            return makeRequest(key, Method.POST, req =>
            {
                req.AddParameter("value", value);
            });
        }

        public EtcdResponse Delete(string key)
        {
            return makeRequest(key, Method.DELETE);
        }

        public EtcdResponse DeleteDir(string key, bool recursive = false)
        {
            return makeRequest(key, Method.DELETE, req =>
            {
                req.AddParameter("dir", "true");
                if (recursive) req.AddParameter("recursive", "true");
            });
        }

        public void Watch(string key, Action<EtcdResponse> followUp, bool recursive = false)
        {
            var requestUrl = _keysRoot.AppendPath(key);
            var getRequest = new RestRequest(requestUrl, Method.GET);
            getRequest.AddParameter("wait", true);
            if (recursive)
            {
                getRequest.AddParameter("recursive", recursive);
            }

            //TODO: Code review this. I know its not a good way to do this
            Task.Run(() =>
            {
                var response = _client.Execute<EtcdResponse>(getRequest);
                followUp(response.Data);
            });
            
        }

        EtcdResponse makeRequest(string key, Method method, Action<IRestRequest> action = null)
        {
            var requestUrl = _keysRoot.AppendPath(key);
            var request = new RestRequest(requestUrl, method);
            
            
            if(action != null) action(request);

            var response = _client.Execute<EtcdResponse>(request);
            return response.Data;
        }
                           

        //TODO: compare and swap options
        //TODO: stats /v2/stats/leader
        //TODO: stats /v2/stats/self
        //TODO: stats /v2/stats/store
    }

    public class EtcdResponse
    {
        public string Action { get; set; }
        public Node Node { get; set; }


        //ttl error
        public int? ErrorCode { get; set; }
        public string Cause { get; set; }
        public int? Index { get; set; }
        public string Message { get; set; }
    }

    public static class EtcResponseHelpers
    {
        public static int EtcIndex(this IRestResponse response)
        {
            return (int)response.Headers.First(x=>x.Name == "X-Etcd-Index").Value;
        }

        public static int EtcRaftIndex(this IRestResponse response)
        {
            return (int)response.Headers.First(x=>x.Name == "X-Raft-Index").Value;
        }

        public static int EtcRaftTerm(this IRestResponse response)
        {
            return (int)response.Headers.First(x => x.Name == "X-Raft-Term").Value;
        }
    }

    public class Node
    {
        public int CreatedIndex { get; set; }
        public string Key { get; set; }
        public int ModifiedIndex { get; set; }
        public string Value { get; set; }
        public int? Ttl { get; set; }
        public DateTime? Expiration { get; set; }
        public List<Node> Nodes { get; set; }
        public bool Dir { get; set; }
    }

    public static class UriHelpers
    {
        public static Uri AppendPath(this Uri uri, string path)
        {
            var path1 = uri.AbsolutePath.TrimEnd(new []
            {
                '/'
            }) + "/" + path;
            return new UriBuilder(uri.Scheme, uri.Host, uri.Port, path1, uri.Query).Uri;
        }
    }
}
