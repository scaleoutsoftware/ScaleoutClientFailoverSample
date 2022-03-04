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
        public static void Main()
        {
            // Note that maxRequestRetries is set to zero in connection strings so failover is faster.
            // See https://static.scaleoutsoftware.com/docs/dotnet_client/articles/configuration/connecting.html

            var pollyClient = new PollyClient<string, string>(
                primaryCacheFactory: () =>
                {
                    var primaryConn = GridConnection.Connect("bootstrapGateways=192.168.1.107:721;maxRequestRetries=0");
                    var primaryCache = new CacheBuilder<string, string>(CacheName, primaryConn)
                                            .Build();
                    return primaryCache;
                },

                failoverCacheFactory: () =>
                {
                    var failoverConn = GridConnection.Connect("bootstrapGateways=192.168.1.60:721;maxRequestRetries=0");
                    var failoverCache = new CacheBuilder<string, string>(CacheName, failoverConn)
                                            .Build();
                    return failoverCache;
                },

                failoverDuration: TimeSpan.FromSeconds(20)
            );

            var response = pollyClient.DoScaleoutRequest(cache => cache.Add("key1", "value1"));

            Console.WriteLine(response.Result);

            while (true)
            {
                try
                {
                    Console.ReadLine();
                    response = pollyClient.DoScaleoutRequest(cache => cache.ReadOrAdd("key1", () => "whee!"));
                    Console.WriteLine(response.Result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                
            }
        }

    }
}