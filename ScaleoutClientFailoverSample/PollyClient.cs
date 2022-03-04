using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Scaleout.Client;
using Polly;
using Polly.CircuitBreaker;
using Polly.Fallback;
using System.Diagnostics;

namespace ScaleoutClientFailoverSample
{
    /// <summary>
    /// A cache access helper that supports temporarily failing over to a backup cluster
    /// of ScaleOut servers in the event of a connectivity problem.
    /// </summary>
    /// <typeparam name="TKey">Type of objects stored in a ScaleOut Cache instance.</typeparam>
    /// <typeparam name="TValue">Type of objects stored in a ScaleOut Cache instance.</typeparam>
    /// <remarks>
    /// <para>
    /// GridConnection.Connect() calls should be performed in the cacheFactory callbacks that
    /// are supplied to the constructor. This allows the initial connection attempt to be 
    /// retried later if the first attempt fails at application startup.
    /// </para>
    /// <para>
    /// Cache methods that perform exclusive locking (Cache.ReadExclusive, Cache.UpdateAndReleaseExclusive, etc.)
    /// should not be used with this class since lock tickets are not shared between different
    /// ScaleOut stores.
    /// </para>
    /// </remarks>
    /// <see href="https://github.com/App-vNext/Polly"/>
    public class PollyClient<TKey, TValue>
    {
        Lazy<Cache<TKey, TValue>> _primaryLazyCache;
        Lazy<Cache<TKey, TValue>> _failoverLazyCache;

        // A Polly CircuitBreaker is stateful, so held as a instance variable.
        CircuitBreakerPolicy<CacheResponse<TKey, TValue>> _breaker;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="primaryCacheFactory">Factory method for connecting to a primary ScaleOut store and building a cache.</param>
        /// <param name="failoverCacheFactory">Factory method for connecting to a failover ScaleOut store and building a cache.</param>
        /// <param name="failoverDuration">Amount of time to wait before retrying the primary cache after a connection problem.</param>
        /// <remarks>
        /// GridConnection.Connect() calls should be performed in the supplied cacheFactory callbacks.
        /// </remarks>
        public PollyClient(Func<Cache<TKey, TValue>> primaryCacheFactory, 
                           Func<Cache<TKey, TValue>> failoverCacheFactory,
                           TimeSpan failoverDuration)
        {
            // Use LazyThreadSafetyMode.PublicationOnly since we don't want Lazy<> to cache an exception
            // that's thrown from a cacheFactory. (If the initial connect attempt fails, we want Lazy<> to
            // re-run the factory again later, after the CircuitBreaker closes.) This is safe since
            // GridConnection.Connect() is thread safe and smart enough to not open multiple connections
            // if called multiple times with the same connection string.
            _primaryLazyCache = new Lazy<Cache<TKey, TValue>>(primaryCacheFactory, LazyThreadSafetyMode.PublicationOnly);
            _failoverLazyCache = new Lazy<Cache<TKey, TValue>>(failoverCacheFactory, LazyThreadSafetyMode.PublicationOnly);

            _breaker = Policy<CacheResponse<TKey, TValue>>
                          .Handle<System.Net.Sockets.SocketException>()
                            .Or<System.IO.IOException>()
                            .Or<Scaleout.Client.ScaleoutServiceUnavailableException>()
                          .CircuitBreaker<CacheResponse<TKey, TValue>>(
                            handledEventsAllowedBeforeBreaking: 1,
                            durationOfBreak: failoverDuration,
                            onBreak: (r, t) => Debug.WriteLine($"Failed over to backup cache due to connectivity error: {r.Exception.Message}"),
                            onReset: () => Debug.WriteLine("Resumed access to primary cache.")
                          );
        }

        /// <summary>
        /// Returns true if this PollyClient instance it currently accessing
        /// the backup cache due to a connectivity failure.
        /// </summary>
        public bool IsFailedOver => _breaker.CircuitState == CircuitState.Open;

        /// <summary>
        /// Issues a ScaleOut request against a primary ScaleOut cache, or, if the primary is unavailable, against a failover cache.
        /// </summary>
        /// <param name="cacheAccessFunc">A function returning a <see cref="CacheResponse{TKey, TValue}"/></param>
        /// <returns>The CacheResponse from the wrapped call.</returns>
        /// <example><code>
        /// var response = pollyClient.DoScaleoutRequest(cache => cache.Read("key1"));
        /// </code></example>
        public CacheResponse<TKey, TValue> DoScaleoutRequest(Func<Cache<TKey, TValue>, CacheResponse<TKey, TValue>> cacheAccessFunc)
        {
            var fallback = Policy<CacheResponse<TKey, TValue>>
                .Handle<Polly.CircuitBreaker.BrokenCircuitException>()
                  .Or<System.Net.Sockets.SocketException>()
                  .Or<System.IO.IOException>()
                  .Or<Scaleout.Client.ScaleoutServiceUnavailableException>()
                .Fallback(() => cacheAccessFunc(_failoverLazyCache.Value));

            var circuitBreakerWithFallback = Policy.Wrap(fallback, _breaker);

            return circuitBreakerWithFallback.Execute(() => cacheAccessFunc(_primaryLazyCache.Value));
        }
        

    }
}
