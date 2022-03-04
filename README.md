This sample illustrates how an app can use the [Polly library](https://github.com/App-vNext/Polly) with the [Scaleout.Client library](https://static.scaleoutsoftware.com/docs/dotnet_client/index.html) to fail over to a second, backup cluster of ScaleOut StateServer hosts.

This combines a Polly *CircuitBreaker* and *Fallback* policy to temporarily redirect requests to a backup store in the event of a connectivity problem. After a specified duration, an attempt will be made to use primary store again.

Users will typically want to use [GeoServer DR](https://www.scaleoutsoftware.com/products/geoserver/) in conjunction with this approach so the backup store has copies of all objects from the primary cluster.