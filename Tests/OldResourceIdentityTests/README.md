# OLD resource identity CPU oracle

Build with `dotnet build Tests/OldResourceIdentityTests/OldResourceIdentityTests.csproj -c Release`.
Run the net48 executable or net10.0 DLL with the installed Bin64 directory as
its first argument. Both targets must pass; process-local string hashing is
part of the stock representation-key contract.

The test links the production CPU identity sources and compares them with the
installed stock allocator, RK function, packed-bit helper, import conversion,
array-key formatter and freelist. It verifies representation reuse/collisions,
common-material bootstrap ordering, name aliases, index truncation, allocation
growth/recycling, descriptor paths, real DDS header metadata and source fallback
semantics. Production calls must leave stock material/proxy/resource-request
counts unchanged. Header/path production is exercised before any platform
renderer exists. A test-only platform stub subsequently permits memory-budget
constants for the stock import-conversion oracle; graphics operations throw.

Stock allocation is invoked only by the isolated reference side and resource
gathering is never called. These tests validate building blocks, not their
future integration with model/LOD lifetime, layout registration or draw queues.
The canonical visible OLD ordering gap remains the four-panel live fixture and
`Tests/CreativeFixtureHarness/Verify-OldResourceOrder.py`.
