using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Scaleout.Client;

namespace ScaleoutClientFailoverSample
{
    public class Program
    {
        private static readonly string CacheName = "MyCache";

        // Note that maxRequestRetries is set to zero in connection strings so failover is faster.
        // See https://static.scaleoutsoftware.com/docs/dotnet_client/articles/configuration/connecting.html

        private static readonly string PrimaryConnString = "bootstrapGateways=192.168.1.107:721;maxRequestRetries=0";
        private static readonly string FailoverConnString = "bootstrapGateways=192.168.1.60:721;maxRequestRetries=0";

        public static void Main()
        {
            var pollyClient = new PollyClient<string, string>(
                primaryCacheFactory: () =>
                {
                    var primaryConn = GridConnection.Connect(PrimaryConnString);
                    var primaryCache = new CacheBuilder<string, string>(CacheName, primaryConn)
                                            .Build();
                    return primaryCache;
                },

                failoverCacheFactory: () =>
                {
                    var failoverConn = GridConnection.Connect(FailoverConnString);
                    var failoverCache = new CacheBuilder<string, string>(CacheName, failoverConn)
                                            .Build();
                    return failoverCache;
                },

                failoverDuration: TimeSpan.FromSeconds(20) // how long to wait before retrying primary after a failure
            );


            // Wrap cache accesses in a DoScaleoutRequest() request call if failover is needed.
            var response = pollyClient.DoScaleoutRequest(cache => cache.Add("key1", "value1"));
            response = pollyClient.DoScaleoutRequest(cache => cache.Read("key1"));

        }

    }
}