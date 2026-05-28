# Changelog

## [1.3.2](https://github.com/Leberkas-org/TurboHTTP/compare/v1.3.1...v1.3.2) (2026-05-28)


### Dependencies

* Bump Verify.XunitV3 from 31.17.0 to 31.18.0 ([f742761](https://github.com/Leberkas-org/TurboHTTP/commit/f74276109b66c0d874cb25b987d891cdac2f8feb))

## [1.3.1](https://github.com/Leberkas-org/TurboHTTP/compare/v1.3.0...v1.3.1) (2026-05-28)


### Dependencies

* Bump Servus.Core from 0.33.10 to 0.33.11 ([d0947e8](https://github.com/Leberkas-org/TurboHTTP/commit/d0947e8f8c8cad8310942133812d39eff908f071))

## [1.3.0](https://github.com/Leberkas-org/TurboHTTP/compare/v1.2.0...v1.3.0) (2026-05-26)


### Features

* add own collection interfaces and dual-implement on adapter classes ([7944d50](https://github.com/Leberkas-org/TurboHTTP/commit/7944d50a9ccf7f868e3aebcc85e004a4c6f8a771))
* add standalone ITurbo*Feature interfaces for ASP.NET Core decoupling ([f420515](https://github.com/Leberkas-org/TurboHTTP/commit/f4205154c11f85ee6ee95b483305c53a9688e0f2))
* complete ServerContextFactory migration to TurboFeatureCollection ([e9d48aa](https://github.com/Leberkas-org/TurboHTTP/commit/e9d48aab1869ab58de4732665d209d14a9469722))
* dual-implement all feature classes with both ASP.NET Core and own interfaces ([7bdaf92](https://github.com/Leberkas-org/TurboHTTP/commit/7bdaf9201b67108b740587110308d072ca0d63f2))
* migrate protocol layer from ASP.NET Core feature interfaces to own ITurbo* interfaces ([1b920c0](https://github.com/Leberkas-org/TurboHTTP/commit/1b920c0a67b15b520c01b009af5d353de131a1df))

## [1.2.0](https://github.com/Leberkas-org/TurboHTTP/compare/v1.1.0...v1.2.0) (2026-05-26)


### Features

* add Host property to TurboWebApplicationBuilder for full builder parity ([7977263](https://github.com/Leberkas-org/TurboHTTP/commit/79772639541da397ddf54845ef1884634e39b5ac))
* Add metadata support to TurboRouteHandlerBuilder and TurboRouteGroupBuilder ([ca7ee9c](https://github.com/Leberkas-org/TurboHTTP/commit/ca7ee9c17e5aa69ab1d4c85aa0ffa4076518d796))
* add TurboEndpointMetadata type with marker interfaces ([9eab9c8](https://github.com/Leberkas-org/TurboHTTP/commit/9eab9c86ecfa5b59c681c3dabd480f14641105b1))
* add TurboServerLimits, Listen(string url), ConfigureEndpointDefaults ([0a63b9d](https://github.com/Leberkas-org/TurboHTTP/commit/0a63b9dce780956cb7cd63ebc25b3d943fbd214e))
* deprecate MapTurbo*/UseTurbo* WebApplication extensions for 2.0 removal ([f81b3ed](https://github.com/Leberkas-org/TurboHTTP/commit/f81b3ed60380ecb569a540ad8a34db56c6217823))
* wire endpoint metadata from route registration through RoutingStage to TurboHttpContext ([e7a0cf7](https://github.com/Leberkas-org/TurboHTTP/commit/e7a0cf7f338f148b0259b05041221be891f241d1))


### Bug Fixes

* improve NotSupportedException messages for WebSockets and Session ([f1fab94](https://github.com/Leberkas-org/TurboHTTP/commit/f1fab949bb7b6c4766327f15136f65eb4bef21ab))
* use N * 1024 size literals in TurboServerOptions per CLAUDE.md ([dfef509](https://github.com/Leberkas-org/TurboHTTP/commit/dfef5096112cc52c0253d91298ced30c13421364))

## [1.1.0](https://github.com/Leberkas-org/TurboHTTP/compare/v1.0.2...v1.1.0) (2026-05-26)


### Features

* **ci:** Add docs build workflow ([1741c44](https://github.com/Leberkas-org/TurboHTTP/commit/1741c44f4a0009cf96f040319eb199aa588fae69))


### Bug Fixes

* **ci:** Remove docs push trigger ([3368641](https://github.com/Leberkas-org/TurboHTTP/commit/3368641ed0f5312b84851b8e59c664c862cec78a))
* Update lock file dependencies ([8399514](https://github.com/Leberkas-org/TurboHTTP/commit/8399514e9a8baed02d7b45b11bbb5a96793088fa))


### Documentation

* Add LikeC4 plugin and diagrams ([25f17bd](https://github.com/Leberkas-org/TurboHTTP/commit/25f17bd8d11322408ae877777e97a57c1a3e0288))

## [1.0.2](https://github.com/Leberkas-org/TurboHTTP/compare/v1.0.1...v1.0.2) (2026-05-25)


### Bug Fixes

* **docs:** Pin likec4 to 1.50.0 ([06c08fb](https://github.com/Leberkas-org/TurboHTTP/commit/06c08fb988c68b85b8b64c1ba9797e518fe94103))

## [1.0.1](https://github.com/Leberkas-org/TurboHTTP/compare/v1.0.0...v1.0.1) (2026-05-25)


### Bug Fixes

* **docs:** correct scenario snippets to use real TurboHTTP APIs ([69d4eb5](https://github.com/Leberkas-org/TurboHTTP/commit/69d4eb5d3e2438027a1cabd6da11bb641cb5f108))
* **docs:** pin likec4 to 1.50.0 to avoid icon resolver bug ([5b4ecff](https://github.com/Leberkas-org/TurboHTTP/commit/5b4ecff5987c9fedef5eaa9991fd33f9028334b2))


### Documentation

* add scenarios showcase page with all 5 scenarios ([40f1f9e](https://github.com/Leberkas-org/TurboHTTP/commit/40f1f9eea5c1a468026ee8d001eb0c21574967c8))
* add scenarios to VitePress navigation ([c7cab1b](https://github.com/Leberkas-org/TurboHTTP/commit/c7cab1be842b7eefdb15664234dd0967b329dc3c))

## [1.0.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.9.2...v1.0.0) (2026-05-25)


### ⚠ BREAKING CHANGES

* pipeline
* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder
* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder()
* **server:** rewrite TurboWebApplication with static factories and interfaces

### Features

* **h2:** implement server-side HTTP/2 response trailers ([fc4df62](https://github.com/Leberkas-org/TurboHTTP/commit/fc4df6242b90facf9ebd010672af2450519b447e))
* **server:** add internal AddTurboKestrel overload accepting TurboServerOptions instance ([7903f13](https://github.com/Leberkas-org/TurboHTTP/commit/7903f131969d85f6ecdbdf59a3ee30cc3932f6f4))
* **server:** add ITurboEndpointRouteBuilder interface ([a9f86d4](https://github.com/Leberkas-org/TurboHTTP/commit/a9f86d4c2cb9658e674cdb803dea3bb195184dbd))
* **server:** add routing extension methods on ITurboEndpointRouteBuilder ([cc7d599](https://github.com/Leberkas-org/TurboHTTP/commit/cc7d599fae6f0235e6826aac301b4bbcbed711a8))
* **server:** add TurboUrlCollection as ICollection&lt;string&gt; wrapper ([9420f5d](https://github.com/Leberkas-org/TurboHTTP/commit/9420f5d0f82dd6877fa5025e30218c2f0b17ec3b))
* **server:** add TurboWebApplicationBuilder ([51efcf7](https://github.com/Leberkas-org/TurboHTTP/commit/51efcf710656f2bbee6d1c4a5bb212e806391198))
* **server:** expose Use, Run, Map, MapWhen directly on TurboWebApplication ([9d83e2f](https://github.com/Leberkas-org/TurboHTTP/commit/9d83e2fed274ef6fe074d592ce3e8686933d6064))


### Bug Fixes

* pipeline ([103f1ce](https://github.com/Leberkas-org/TurboHTTP/commit/103f1ce3a431ee06e34df74f69d55c7a9ea0a4e3))
* switch NuGet publish from Trusted Publishing to API key auth ([2f4bbad](https://github.com/Leberkas-org/TurboHTTP/commit/2f4bbad2ccdf79fee983ea7452e63839d38fc2c9))


### Refactoring

* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder() ([f4cf7af](https://github.com/Leberkas-org/TurboHTTP/commit/f4cf7af822489518c717bd148caab4d2a3d7f8e8))
* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder ([cbfac65](https://github.com/Leberkas-org/TurboHTTP/commit/cbfac65c9b281a202ee278dc372808142f03d862))
* **server:** return ITurboPipelineBuilder from pipeline methods ([4fbbc72](https://github.com/Leberkas-org/TurboHTTP/commit/4fbbc72168e243678d705ebc5b7c4495cf971805))
* **server:** rewrite TurboWebApplication with static factories and interfaces ([fb4970e](https://github.com/Leberkas-org/TurboHTTP/commit/fb4970eecbb9d47744a1636b3cd94852bd7e70ea))

## [1.0.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.9.2...v1.0.0) (2026-05-25)


### ⚠ BREAKING CHANGES

* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder
* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder()
* **server:** rewrite TurboWebApplication with static factories and interfaces

### Features

* **h2:** implement server-side HTTP/2 response trailers ([fc4df62](https://github.com/Leberkas-org/TurboHTTP/commit/fc4df6242b90facf9ebd010672af2450519b447e))
* **server:** add internal AddTurboKestrel overload accepting TurboServerOptions instance ([7903f13](https://github.com/Leberkas-org/TurboHTTP/commit/7903f131969d85f6ecdbdf59a3ee30cc3932f6f4))
* **server:** add ITurboEndpointRouteBuilder interface ([a9f86d4](https://github.com/Leberkas-org/TurboHTTP/commit/a9f86d4c2cb9658e674cdb803dea3bb195184dbd))
* **server:** add routing extension methods on ITurboEndpointRouteBuilder ([cc7d599](https://github.com/Leberkas-org/TurboHTTP/commit/cc7d599fae6f0235e6826aac301b4bbcbed711a8))
* **server:** add TurboUrlCollection as ICollection&lt;string&gt; wrapper ([9420f5d](https://github.com/Leberkas-org/TurboHTTP/commit/9420f5d0f82dd6877fa5025e30218c2f0b17ec3b))
* **server:** add TurboWebApplicationBuilder ([51efcf7](https://github.com/Leberkas-org/TurboHTTP/commit/51efcf710656f2bbee6d1c4a5bb212e806391198))
* **server:** expose Use, Run, Map, MapWhen directly on TurboWebApplication ([9d83e2f](https://github.com/Leberkas-org/TurboHTTP/commit/9d83e2fed274ef6fe074d592ce3e8686933d6064))


### Refactoring

* **server:** remove HostBuilderExtensions, migrate to TurboWebApplication.CreateBuilder() ([f4cf7af](https://github.com/Leberkas-org/TurboHTTP/commit/f4cf7af822489518c717bd148caab4d2a3d7f8e8))
* **server:** rename ITurboPipelineBuilder to ITurboApplicationBuilder ([cbfac65](https://github.com/Leberkas-org/TurboHTTP/commit/cbfac65c9b281a202ee278dc372808142f03d862))
* **server:** return ITurboPipelineBuilder from pipeline methods ([4fbbc72](https://github.com/Leberkas-org/TurboHTTP/commit/4fbbc72168e243678d705ebc5b7c4495cf971805))
* **server:** rewrite TurboWebApplication with static factories and interfaces ([fb4970e](https://github.com/Leberkas-org/TurboHTTP/commit/fb4970eecbb9d47744a1636b3cd94852bd7e70ea))

## [0.9.2](https://github.com/Leberkas-org/TurboHTTP/compare/v0.9.1...v0.9.2) (2026-05-24)


### Documentation

* update NuGet metadata and fix README links ([f4882d4](https://github.com/Leberkas-org/TurboHTTP/commit/f4882d4b552970bef3f44e449dfdae066b74b55d))

## [0.9.1](https://github.com/Leberkas-org/TurboHTTP/compare/v0.9.0...v0.9.1) (2026-05-24)


### Bug Fixes

* **ci:** upgrade Node to 22 and regenerate docs lockfile ([e599b78](https://github.com/Leberkas-org/TurboHTTP/commit/e599b781d2bd9a2e732345234480e11a966ed62f))


### Performance

* **routing:** eliminate per-request allocations in route matching ([5ac31d2](https://github.com/Leberkas-org/TurboHTTP/commit/5ac31d21cf1300a647b30515a85ed88b794503a3))
* **routing:** replace linear scan with dictionary lookup in RouteTable ([b3be40a](https://github.com/Leberkas-org/TurboHTTP/commit/b3be40a7d7a7683b271e0f59a6ac12ce77d394da))
* **server:** add server-side micro and throughput benchmarks ([b6bf348](https://github.com/Leberkas-org/TurboHTTP/commit/b6bf348b51bc0b3f20ee5c810e879918c0d8336d))


### Documentation

* Update README links to new organization ([770150c](https://github.com/Leberkas-org/TurboHTTP/commit/770150cfd0af3d295e1d202ef05e78984d5dffc4))


### Dependencies

* bump actions/download-artifact from 4 to 8 ([262114c](https://github.com/Leberkas-org/TurboHTTP/commit/262114ce2377df106bf34f0a9fe2a6d5cdb43aa4))
* bump actions/upload-artifact from 4 to 7 ([bdd7d51](https://github.com/Leberkas-org/TurboHTTP/commit/bdd7d518501ba6029111a34a766023a4ce1e3be0))
* Bump the akka group with 1 update ([4edbee8](https://github.com/Leberkas-org/TurboHTTP/commit/4edbee8a16572580be06385984939448aacaebd9))
* Bump Verify.XunitV3 from 31.16.3 to 31.17.0 ([a2933ed](https://github.com/Leberkas-org/TurboHTTP/commit/a2933ed059af7763db8b93c34929fc4f5f0c48aa))

## [0.9.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.8.0...v0.9.0) (2026-05-24)


### Features

* add code coverage to integration tests ([2f1d7bc](https://github.com/Leberkas-org/TurboHTTP/commit/2f1d7bcff531589e13372554a70bedcfbc479f22))
* add generic HttpConnectionStageLogic&lt;TSM&gt; base stage ([4901d10](https://github.com/Leberkas-org/TurboHTTP/commit/4901d1004a5fb6e662c94c1c0d9032a9d9b9c05b))
* add pipeline tracing across all BidiStages and stage logic ([3d31bbe](https://github.com/Leberkas-org/TurboHTTP/commit/3d31bbe540d02d6551e13d113ce49afadf0ace92))
* add QUIC transport implementation ([780d0c0](https://github.com/Leberkas-org/TurboHTTP/commit/780d0c0c6e3c685c95b4be29105053c3e366cbd7))
* add RequestFault helper for shared request error handling ([5c7f491](https://github.com/Leberkas-org/TurboHTTP/commit/5c7f491e5af5c2d56f2d2f8c9f30114c58811a52))
* add Servus.Akka.TestKit dependency ([98bffd1](https://github.com/Leberkas-org/TurboHTTP/commit/98bffd190ad144fb30682ac5bfad143724978db6))
* add Servus.Akka.Transport listener ([70ad8bf](https://github.com/Leberkas-org/TurboHTTP/commit/70ad8bf99cfffde2f09148cfc3c8bf59df6aa613))
* add ServusTrace integration ([722ea70](https://github.com/Leberkas-org/TurboHTTP/commit/722ea7047977854b8b9cd361e0f22bc580ea8b87))
* add TCP transport implementation ([ebf6689](https://github.com/Leberkas-org/TurboHTTP/commit/ebf66890bb54ab84565b32456b516f64b449606e))
* **bench:** add microbenchmark project with baseline comparisons ([3d5a6b0](https://github.com/Leberkas-org/TurboHTTP/commit/3d5a6b0ae635ffb3bdb6d91d9c249147181f383c))
* **body:** add GetBodyStream() to LineBased IBodyDecoder implementations ([f0578b1](https://github.com/Leberkas-org/TurboHTTP/commit/f0578b1cf0804fb5b2fb3e06bbd6d06a528c2965))
* **body:** add Stream-based Start/Create overloads to LineBased body encoders ([49c2e42](https://github.com/Leberkas-org/TurboHTTP/commit/49c2e42bf1cd139231107e42d18d3e7ea1ee8eb9))
* **body:** add Stream-based Start/Create overloads to Multiplexed body encoders ([0a97365](https://github.com/Leberkas-org/TurboHTTP/commit/0a97365554ec860db479a2adf4c655ca77f8e025))
* **context:** add WhenHeadersReady signal to TurboHttpResponseBodyFeature ([e604a6c](https://github.com/Leberkas-org/TurboHTTP/commit/e604a6cf146479f0c6abba289e5d5bb32f8117a8))
* **context:** create TurboRequestBodyFeature and align response body with Kestrel ([4738712](https://github.com/Leberkas-org/TurboHTTP/commit/473871230437f749c8119e6fd8459ac2ae294768))
* define IHttpStateMachine interface and expand IStageOperations ([6876159](https://github.com/Leberkas-org/TurboHTTP/commit/68761591d4a763b9d9c3448ad103fda09edb4ccc))
* **diagnostics:** add HexDumpFormatter for Kestrel-style wire dumps ([cbcfb5a](https://github.com/Leberkas-org/TurboHTTP/commit/cbcfb5af406764faeaf80df02b8bbb042c0401f7))
* **encoders:** add TurboHttpContext overloads to all 4 server encoders ([d314a86](https://github.com/Leberkas-org/TurboHTTP/commit/d314a86e611c3f7d60722d062e9d850fd7592538))
* **features:** add ITlsHandshakeFeature interface and implementation ([4207222](https://github.com/Leberkas-org/TurboHTTP/commit/42072228166b95f38017a154e6071a277065d75a))
* **h11:** implement IHttpStateMachine on Http11 StateMachine ([5420762](https://github.com/Leberkas-org/TurboHTTP/commit/5420762806eba3f4b22bff8ed71599af19c20284))
* **h2:** wire InitialStreamWindowSize into server SETTINGS frame ([861a7c4](https://github.com/Leberkas-org/TurboHTTP/commit/861a7c4988a8bcb1862ebee6552ae0f2dedc2d12))
* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/Leberkas-org/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/Leberkas-org/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/Leberkas-org/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/Leberkas-org/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/Leberkas-org/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/Leberkas-org/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/Leberkas-org/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/Leberkas-org/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/Leberkas-org/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/Leberkas-org/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))
* **http10:** add Http10ServerDecoder.GetRequestFeature() ([0804b1a](https://github.com/Leberkas-org/TurboHTTP/commit/0804b1a4158582c3c4f47cade4a214f352677a7b))
* **http11:** add h2c upgrade detection with IProtocolSwitchCapable signaling ([1e60d31](https://github.com/Leberkas-org/TurboHTTP/commit/1e60d3163e1bbadf92c67491799173c4704a2f4a))
* **http11:** start body encoder in server OnResponse for streaming support ([984c503](https://github.com/Leberkas-org/TurboHTTP/commit/984c503166845e226efabe68beb8233d665b954e))
* **http2:** add Http2ServerDecoder.DecodeHeadersToFeature() ([2f990ce](https://github.com/Leberkas-org/TurboHTTP/commit/2f990cebdc0abbd9dba607485b4d937b46a663fe))
* **http3:** add Http3ServerDecoder.DecodeHeadersToFeature() ([7d6a356](https://github.com/Leberkas-org/TurboHTTP/commit/7d6a35658dd884eb510e131756e2580ab65a6cad))
* **lifecycle:** add ConsumerActor for per-consumer ingress and response sink ([b135cd3](https://github.com/Leberkas-org/TurboHTTP/commit/b135cd386d7484cfb2b379840282bd913a95c542))
* **lifecycle:** extract TLS metadata ([07fd711](https://github.com/Leberkas-org/TurboHTTP/commit/07fd71114421703629103b74ddbe6639cf20cb15))
* **protocol:** add HeaderRouter.ApplyToHeaderDictionary for flat header writing ([a839e74](https://github.com/Leberkas-org/TurboHTTP/commit/a839e74815d480e39d8ad763328eb815f6acb094))
* **protocol:** add ProtocolNegotiatingStateMachine with ALPN and preface detection ([befd348](https://github.com/Leberkas-org/TurboHTTP/commit/befd3489bd741ad2412b4df46f62865031d588ea))
* **protocol:** extract encoder/decoder options for HTTP/2 and HTTP/3 ([138b68a](https://github.com/Leberkas-org/TurboHTTP/commit/138b68aed7b16ffc6b9706857a43b6461b04b3dd))
* **routing:** add Count property to EntityResponseMapperCollection ([fa65d3d](https://github.com/Leberkas-org/TurboHTTP/commit/fa65d3db0f5bd70bf9a3fa828266e56fb0e7cfac))
* **routing:** extend EntityMethodConfig with endpoint mappers and tell handler ([7891c56](https://github.com/Leberkas-org/TurboHTTP/commit/7891c56d89c0c5eb632be32c1d1e7772456ad433))
* **routing:** map new TLS options in EndpointResolver ([fcc8c6e](https://github.com/Leberkas-org/TurboHTTP/commit/fcc8c6e8d0a774d0faa92cc60e9f6c359b2a2cc2))
* **routing:** push response context on StartAsync before handler completes ([c968ee1](https://github.com/Leberkas-org/TurboHTTP/commit/c968ee123b15ef31d194b156ca3f7c8fe49fba5c))
* **routing:** update EntityDispatcher with two-tier mapper lookup and pluggable tell handler ([7bda4a1](https://github.com/Leberkas-org/TurboHTTP/commit/7bda4a172f887a75ea614f7cef3cb3d71edb5dfa))
* **semantics:** add RFC-compliant HTTP semantic validators ([8566489](https://github.com/Leberkas-org/TurboHTTP/commit/8566489631ec8e4d4de0fc02252fce8b9188d90e))
* **server:** add ClientCertificateMode and ServerCertificateSelector to TurboHttpsOptions ([3ba4a8c](https://github.com/Leberkas-org/TurboHTTP/commit/3ba4a8c91fe6ee8ab3e159d36c17dd7df23f8951))
* **server:** add ConnectionLoggingBidiStage for wire-level hex dump logging ([02dc33c](https://github.com/Leberkas-org/TurboHTTP/commit/02dc33cdc3e26c3a728b2b02a6e816eac1f74f3f))
* **server:** add DelayCertificate renegotiation support ([3874559](https://github.com/Leberkas-org/TurboHTTP/commit/3874559468e42b8edf027c08757d86a340a69098))
* **server:** add drain protocol to body decoders for request pipelining ([e16cfed](https://github.com/Leberkas-org/TurboHTTP/commit/e16cfedaad66a8ba7a3de948f7455c3ebe3834b5))
* **server:** add IHttpRequestLifetimeFeature, IHttpRequestIdentifierFeature, IHttpResetFeature ([fcfe851](https://github.com/Leberkas-org/TurboHTTP/commit/fcfe851609662941432bd45d5f1da13b0b941b1d))
* **server:** add IsAsk/IsTell to TurboEntityMethodBuilder, deprecate AcceptedResponse ([ce010a3](https://github.com/Leberkas-org/TurboHTTP/commit/ce010a3c732b724c3ca5d7025896f7dcc935c72b))
* **server:** add minimal TurboHttpContext constructor for protocol-layer creation ([1b43a73](https://github.com/Leberkas-org/TurboHTTP/commit/1b43a7303defa39f8e88b1a02ecd7ad6fc07cfc3))
* **server:** add request tracking and content classification to protocol layer ([04829db](https://github.com/Leberkas-org/TurboHTTP/commit/04829dbdc34b966cad19860ca788ffb1e3226921))
* **server:** add TurboEntityAskBuilder with Response, Produces, and WithTimeout support ([a093d79](https://github.com/Leberkas-org/TurboHTTP/commit/a093d79809f6a6e648083cd46e618ac893dc02b8))
* **server:** add TurboEntityTellBuilder with Response and Produces support ([e069149](https://github.com/Leberkas-org/TurboHTTP/commit/e069149b187059798ec58402c6ec8c98169a1483))
* **server:** add TurboTlsCallbackOptions and TurboTlsCallbackContext ([a1ddcf7](https://github.com/Leberkas-org/TurboHTTP/commit/a1ddcf76e16571266dc3f87a66d4db56fd1afab5))
* **server:** add UseConnectionLogging() and wire through to ConnectionActor ([b46eb6f](https://github.com/Leberkas-org/TurboHTTP/commit/b46eb6f7cd028fc27bd247eb4dffbe57c3042268))
* **server:** add UseHttps(TurboTlsCallbackOptions) overload to TurboListenOptions ([1217894](https://github.com/Leberkas-org/TurboHTTP/commit/1217894ae8fcbaebc195ad9a84c40cce4a6489cd))
* **server:** enforce MaxConcurrentConnections per listener ([eaa333a](https://github.com/Leberkas-org/TurboHTTP/commit/eaa333abb9818286ec9dbca521d79e942eeb396d))
* **server:** implement entity gateway with ASP.NET-style middleware pipeline ([04d4b50](https://github.com/Leberkas-org/TurboHTTP/commit/04d4b5013c20d6b1cf5ae6555218532ed4cfaf81))
* **server:** Inject IMaterializer into HttpContext ([a01f5bf](https://github.com/Leberkas-org/TurboHTTP/commit/a01f5bf853822caecd0c3cdde5e23b14a4a55a96))
* **server:** populate ITlsHandshakeFeature on HttpContext feature ([f50dd4c](https://github.com/Leberkas-org/TurboHTTP/commit/f50dd4c84be3f13c4ab8478c4ee7216e70b5f3db))
* **server:** split Http2ServerOptions.InitialWindowSize into connection and stream properties ([e19191a](https://github.com/Leberkas-org/TurboHTTP/commit/e19191ad96d9a85169eca599b952eb1723a097ae))
* **servus:** add PipeReaderSourceStage and StreamSource factory ([075c6b2](https://github.com/Leberkas-org/TurboHTTP/commit/075c6b20172b2df2ddf7f599b5d2cfeb4398c23f))
* **sse:** add AsEventStream extension for reactive SSE consumption ([3ac50ec](https://github.com/Leberkas-org/TurboHTTP/commit/3ac50ec4697855402d36db860e60804375e2a44e))
* **sse:** add ServerSentEvent and AsEventStream ([794a3c6](https://github.com/Leberkas-org/TurboHTTP/commit/794a3c66a8fc8fa6d949925ad28a8feb497e5c44))
* **sse:** implement ServerSentEvent parser GraphStage with full RFC compliance ([7742ec0](https://github.com/Leberkas-org/TurboHTTP/commit/7742ec0170c94cdfc1ef25dfd2dd10137862a30e))
* **streams:** add NegotiatingServerEngine ([1b01747](https://github.com/Leberkas-org/TurboHTTP/commit/1b017470ebabea0812fca9db04c2925c6bdfd03d))
* **streams:** add Pipe stages for bidirectional IO bridging ([9865763](https://github.com/Leberkas-org/TurboHTTP/commit/9865763a79ff4560f7dc1022139ca4d432fa96a3))
* **tests:** add IntegrationTests.E2E project with SSE round-trip tests ([83fa651](https://github.com/Leberkas-org/TurboHTTP/commit/83fa65140a2daf8a5bd8caa787df51e7e7d25f0a))
* **tests:** add IntegrationTests.Server project with basic HTTP tests ([0289df1](https://github.com/Leberkas-org/TurboHTTP/commit/0289df17666cbb323989135692679d145214ee7c))
* **tests:** add ServerTestContextBuilder for fluent test context creation ([b115828](https://github.com/Leberkas-org/TurboHTTP/commit/b1158288d9aff1837c4a53f2d2ac665688977064))
* **tests:** add TurboServerFixture for server and E2E integration tests ([05c4e49](https://github.com/Leberkas-org/TurboHTTP/commit/05c4e49078f1245c049415a0c5902d19bd5f0192))
* **tests:** configure xunit parallelization and timeouts ([143003f](https://github.com/Leberkas-org/TurboHTTP/commit/143003f207ae992e8c94e1ea8d32b48f3597c234))
* **tests:** Migrate integration tests to new structure ([f981f7a](https://github.com/Leberkas-org/TurboHTTP/commit/f981f7abb306d65b48c91ebae2395351d3d9d1f4))
* **transport:** add ClientCertificateMode enum ([06de0c0](https://github.com/Leberkas-org/TurboHTTP/commit/06de0c0532c75d76130e74539f815b409958af0c))
* **transport:** add ClientCertificateMode, HandshakeCallback, ServerCertificateSelector ([a98c9be](https://github.com/Leberkas-org/TurboHTTP/commit/a98c9be99a7d3885b2a6de2402c09c75bd6bcdae))
* **transport:** add TlsHandshakeContext, TlsHandshakeCallback, TlsConnectionResult ([3ba8d70](https://github.com/Leberkas-org/TurboHTTP/commit/3ba8d70b6c1490d0b53213de608fa3313b6875bd))
* **transport:** add TransportTlsState inbound message for DelayCertificate ([98ee596](https://github.com/Leberkas-org/TurboHTTP/commit/98ee596603fe70f2ac053ab0518ac01834e5d070))
* **transport:** extend SecurityInfo with NegotiatedCipherSuite and HostName ([d38c319](https://github.com/Leberkas-org/TurboHTTP/commit/d38c319ac4ba442125798481ccdb2064664b25d4))
* **transport:** rewrite TcpListenerStage handshake with 3 paths ([a84d189](https://github.com/Leberkas-org/TurboHTTP/commit/a84d1898cbe35f098bac7f891d1b64833baefd81))
* **TurboHTTP.Server:** add Akka.Streams-based HTTP server ([2cab2cf](https://github.com/Leberkas-org/TurboHTTP/commit/2cab2cfe0705f24b162ace599dedab3834418929))
* **vault:** convert all 393 RFC section files to VAULT_STYLE_GUIDE-compliant Markdown ([b9c3a81](https://github.com/Leberkas-org/TurboHTTP/commit/b9c3a81cbab00b7b73d65b2737a8a7cf29bcd12b))


### Bug Fixes

* add exception safety to all pipeline onPush handlers ([7e481e4](https://github.com/Leberkas-org/TurboHTTP/commit/7e481e4bf0fe5418867b7d88d811764a2f082c03))
* align test expectations with corrected server option defaults ([ced7837](https://github.com/Leberkas-org/TurboHTTP/commit/ced7837300852b53174c885784bfc29120785c9d))
* checkout with lfs ([7a470ab](https://github.com/Leberkas-org/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **ci:** adjust release manifest directory ([44980a5](https://github.com/Leberkas-org/TurboHTTP/commit/44980a5bc10fec9ab44656b037e3ed621d14e6dd))
* **ci:** integrate deps commit type ([5f88452](https://github.com/Leberkas-org/TurboHTTP/commit/5f884529c0df9fa64889ea2ae38c42b0fd27a631))
* **commitlint:** ignore dependabot commits ([f3a662a](https://github.com/Leberkas-org/TurboHTTP/commit/f3a662aced507f71057ba1d88416c018cbe42e88))
* EntityDispatcher tests ([e89a3f2](https://github.com/Leberkas-org/TurboHTTP/commit/e89a3f2f34d74933dbe72dc1e87af1458081ddca))
* fix namespace errors ([bc5d0f9](https://github.com/Leberkas-org/TurboHTTP/commit/bc5d0f91e29e6b5e73b859f03af3fe4192313a54))
* **h11:** handle reconnect failure gracefully instead of failing stage ([dc6066a](https://github.com/Leberkas-org/TurboHTTP/commit/dc6066ae9058c0ea7c1b1a3867392dc472927e4d))
* **h11:** use OnComplete for reconnect exhaustion instead of OnFail ([eafefdd](https://github.com/Leberkas-org/TurboHTTP/commit/eafefdd0bd82e521523a63a0d024f7621990bbea))
* **h3:** enable QUIC/HTTP3 integration tests on Docker ([d940a60](https://github.com/Leberkas-org/TurboHTTP/commit/d940a60499afe142f31994a2090d1be35ed196c5))
* **h3:** improve control stream stability ([153a37b](https://github.com/Leberkas-org/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** open QUIC stream before sending request frames ([8ebd05d](https://github.com/Leberkas-org/TurboHTTP/commit/8ebd05dc4557c1a8f318b98d4a689d76e6f63a13))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/Leberkas-org/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **http2,http3:** treat absent Content-Length as streaming response with body ([bd4767e](https://github.com/Leberkas-org/TurboHTTP/commit/bd4767e9bd3557e7a61792873d506431de379c9e))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/Leberkas-org/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/Leberkas-org/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* minor fixes ([2d62179](https://github.com/Leberkas-org/TurboHTTP/commit/2d62179e9c8a91c4591f50d33bfb3b39ff8921bc))
* minor fixes ([f1cb795](https://github.com/Leberkas-org/TurboHTTP/commit/f1cb79575a48d17e6ac6f3392640142662c3b6bf))
* minor transport fix ([9b1bba2](https://github.com/Leberkas-org/TurboHTTP/commit/9b1bba223aeffc63b87dd63db3714c4d54a53e80))
* obsolete ctor ([acffe6c](https://github.com/Leberkas-org/TurboHTTP/commit/acffe6cc87d339e04765f5b73393c28caf1b14d8))
* public api changes ([2d6dfa4](https://github.com/Leberkas-org/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/Leberkas-org/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))
* **quic:** resolve deadlock in AcceptInboundStreamAsync test ([3e1eacb](https://github.com/Leberkas-org/TurboHTTP/commit/3e1eacb9819fd3e5c0b60b419f741bd49083eefe))
* **quic:** use IPEndPoint for IP address hosts in QuicClientProvider ([0d49238](https://github.com/Leberkas-org/TurboHTTP/commit/0d492383e48411ceb906fba187bef31ac43a7a57))
* **readme:** Correct workflow badges ([1392385](https://github.com/Leberkas-org/TurboHTTP/commit/13923855b98ce487754d43baac42b13bdd720c32))
* release please ([c1c7ae3](https://github.com/Leberkas-org/TurboHTTP/commit/c1c7ae30a1841782fe2440d2c2353747514b56d7))
* **release:** correct config paths ([a6ad77e](https://github.com/Leberkas-org/TurboHTTP/commit/a6ad77ee7bae28faa1515ac9cd7562ece4731cff))
* **request-feature:** ensure Host header fallback from RequestUri ([25979e1](https://github.com/Leberkas-org/TurboHTTP/commit/25979e19acb349bea394ab59faf32a27a7e83d68))
* reset release-please version to 0.8.0 ([3f465d8](https://github.com/Leberkas-org/TurboHTTP/commit/3f465d8c14af2df1d2c2e77061ac6b2fa35c2e2e))
* resolve H11 POST redirect deadlock and enable skipped acceptance tests ([f5e0564](https://github.com/Leberkas-org/TurboHTTP/commit/f5e05649728fa5bf36b521125bdbbe2fb077f050))
* **routing:** remove broken RequestBinder for HttpRequestMessage parameter type ([c3ec9c0](https://github.com/Leberkas-org/TurboHTTP/commit/c3ec9c0ef23867665a5493db63158014e6e71b95))
* **security:** address CodeQL findings for cookie injection and open redirect ([6bfb4ee](https://github.com/Leberkas-org/TurboHTTP/commit/6bfb4ee87ac4f133d2c4cc4ec2c64f6a69647971))
* **security:** prevent open redirect in HandleRedirectTo and harden Redirect() ([57b0b88](https://github.com/Leberkas-org/TurboHTTP/commit/57b0b885ddda319b7e5cd9b821ebeaa766912360))
* **security:** reject CRLF and normalize URLs in TurboHttpResponse.Redirect() ([81e1aa9](https://github.com/Leberkas-org/TurboHTTP/commit/81e1aa9f56e04fcec5bbba4b9aa5d68b891ed22d))
* **security:** sanitize response header values in test httpbin endpoint ([5a34979](https://github.com/Leberkas-org/TurboHTTP/commit/5a3497994cbf12f134d91396dc7224502711cb53))
* **server:** eliminate listener bind race condition via materialized Task ([b16dbc8](https://github.com/Leberkas-org/TurboHTTP/commit/b16dbc8dbcc4399427b1ac097a7f0cc73b249707))
* **server:** thread IServiceProvider and TurboConnectionInfo through server pipeline ([abce087](https://github.com/Leberkas-org/TurboHTTP/commit/abce0870d865b5600e489e799477c6f11b392d5c))
* **Servus.Akka:** use async dispose and cancellation ([ac318f4](https://github.com/Leberkas-org/TurboHTTP/commit/ac318f4ad3939acfb76f9a3f3e142b87de7d40b1))
* **sse:** align formatter with Kestrel's SseFormatter implementation ([a13b9ef](https://github.com/Leberkas-org/TurboHTTP/commit/a13b9eff960bd4fae3f825192f825f3d94406578))
* **sse:** align SSE formatter with WHATWG spec ([abaef51](https://github.com/Leberkas-org/TurboHTTP/commit/abaef511e0da16e8b063e46a9e36a2c555515352))
* **sse:** align SSE parser with WHATWG spec and skip SSE tests on Docker ([a46d70f](https://github.com/Leberkas-org/TurboHTTP/commit/a46d70f8f9d79b66cc8134db17e75df1589e01ee))
* **sse:** remove duplicate XML documentation in Extensions.cs ([0100f12](https://github.com/Leberkas-org/TurboHTTP/commit/0100f12a4faffb5dc593c0c0c277f1a5a3a11e82))
* **test:** generate HTTPS test certificate programmatically ([7d5a954](https://github.com/Leberkas-org/TurboHTTP/commit/7d5a95464b8a82d79c5414d37c9a8f53f3e6be74))
* **tests:** add Materializer property to FakeServerOps and SwitchCapableOps ([83f9002](https://github.com/Leberkas-org/TurboHTTP/commit/83f9002af679d7f088d70aea94a6d9f28fd95e7d))
* **tests:** consolidate server test fakes and fix all server-side test failures ([d531035](https://github.com/Leberkas-org/TurboHTTP/commit/d53103517e29776e38ab8bbec44a45798715d321))
* **tests:** let Kestrel pick HTTPS port to avoid port conflicts ([7a8bfe0](https://github.com/Leberkas-org/TurboHTTP/commit/7a8bfe00e1807cec5116a522193341101e4b57f4))
* **tests:** Use 127.0.0.1 for H3 and parallelize tests ([9161dd0](https://github.com/Leberkas-org/TurboHTTP/commit/9161dd06516ad24960b782dcde1d72960c071088))
* **tests:** use fresh HttpClient to avoid connection pool reuse in timeout test ([be74295](https://github.com/Leberkas-org/TurboHTTP/commit/be742952ed5285221848304d8f2774f36cfee408))
* **transport:** make TLS handshake async with configurable timeout ([92bc679](https://github.com/Leberkas-org/TurboHTTP/commit/92bc679bf74ed0afc2cde349f5f31961f4978c75))
* wire TurboHttpRequest.BodySource to ITurboRequestBodyFeature ([132a30d](https://github.com/Leberkas-org/TurboHTTP/commit/132a30d903df31277890dca088d5c6c2d5259180))


### Performance

* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/Leberkas-org/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* enhance HTTP/2 and HTTP/3 transport performance and streaming ([74d0b30](https://github.com/Leberkas-org/TurboHTTP/commit/74d0b30d7105482d1933354c09ea6aedd9cfd5f3))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/Leberkas-org/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/Leberkas-org/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/Leberkas-org/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/Leberkas-org/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/Leberkas-org/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/Leberkas-org/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))
* **server:** add Date and Content-Length header caches ([861c436](https://github.com/Leberkas-org/TurboHTTP/commit/861c4365e6ff75f7924a699f2312d5b87eede9c2))
* **server:** implement context pooling with reset semantics ([faf7300](https://github.com/Leberkas-org/TurboHTTP/commit/faf7300b7d5fd51c83359a79f04ee7b785bfe88e))
* **tests:** parallelize integration tests and fix H3 infrastructure ([0d93389](https://github.com/Leberkas-org/TurboHTTP/commit/0d9338915af97f7ad1623b49b38f671cb0441acc))


### Documentation

* add Akka.Streams integration scenario for client ([357ce16](https://github.com/Leberkas-org/TurboHTTP/commit/357ce1697825c5cc1fbae2fbcda5dbe627d78095))
* add Architecture as top-level nav with Client/Server groups ([6a9866a](https://github.com/Leberkas-org/TurboHTTP/commit/6a9866a3ba4b0f7ba35cb36e6f6288f54eb5b4ff))
* add dynamic protocol negotiation design spec ([ab58122](https://github.com/Leberkas-org/TurboHTTP/commit/ab58122bb80d5251958399019bf06fe133dbc420))
* add dynamic protocol negotiation implementation plan ([45ba63e](https://github.com/Leberkas-org/TurboHTTP/commit/45ba63eb9c5ad84b2901cddd0d6bb311de820c48))
* add eager pipeline materialization spec and plan ([a77a1f4](https://github.com/Leberkas-org/TurboHTTP/commit/a77a1f434153fc7056a25d13f2d0fb93624468e8))
* add HomePage and CodeTabs Vue components ([909d3b7](https://github.com/Leberkas-org/TurboHTTP/commit/909d3b7b948c3d3363dd3bb3b99f3e5fc13c726c))
* add LikeC4 server architecture diagrams ([10b16a5](https://github.com/Leberkas-org/TurboHTTP/commit/10b16a5163bcbe50d4b3155d348f7a8dc748d4c7))
* add planning documents and update CLAUDE.md ([a3c59d1](https://github.com/Leberkas-org/TurboHTTP/commit/a3c59d1b4a3d6b981316799a72031c1d74905b5b))
* add real-world client scenario examples ([0a4dab7](https://github.com/Leberkas-org/TurboHTTP/commit/0a4dab7fa9d6b9e861147b169325bfd9ce2f1960))
* add real-world server scenario examples ([bd68625](https://github.com/Leberkas-org/TurboHTTP/commit/bd6862575bc6cb677e8707c682c7ec51197a706a))
* add server test coverage map with risk-based prioritization ([63c5081](https://github.com/Leberkas-org/TurboHTTP/commit/63c5081d1549c054e7b29cbf1794ad164ad0de59))
* add StreamTests consolidation and microbenchmark plans ([6cbcf00](https://github.com/Leberkas-org/TurboHTTP/commit/6cbcf004fad93b74ca4ffd57599127de3d476e79))
* add symmetric server architecture pages (pipeline, engines, extending) ([34dc02a](https://github.com/Leberkas-org/TurboHTTP/commit/34dc02abf22912494e3ec5d06752c16d961d2989))
* add test restructuring spec ([a8c4dd2](https://github.com/Leberkas-org/TurboHTTP/commit/a8c4dd2fce3df4d49eaf1463bbfddf82b7f55644))
* add VitePress redesign implementation plan ([bb535e5](https://github.com/Leberkas-org/TurboHTTP/commit/bb535e5c67e8a72deb8e494f36e4e0d262f5f014))
* add VitePress redesign spec ([390a611](https://github.com/Leberkas-org/TurboHTTP/commit/390a611997139e2ba0ee13299fb3156ed14bedcf))
* align LikeC4 models and docs with actual class names ([75a0a0f](https://github.com/Leberkas-org/TurboHTTP/commit/75a0a0f0e4e5bf7a2233baa96b181b55822a1dd3))
* complete API reference split (server, entity gateway, overview) ([d9a7471](https://github.com/Leberkas-org/TurboHTTP/commit/d9a747165870436f3f3e0ed376fd7478e9fdf927))
* correct server narrative — standalone server, not Kestrel-basedr ([3aa50bb](https://github.com/Leberkas-org/TurboHTTP/commit/3aa50bbbf595226090f1b28ba30444befb5624d6))
* create Getting Started section with quick starts and architecture overview ([320afd0](https://github.com/Leberkas-org/TurboHTTP/commit/320afd0ee92db821e303171940e0c8b51cb51606))
* create split API reference pages (client, options, features) ([423a479](https://github.com/Leberkas-org/TurboHTTP/commit/423a479d75fdf2dc9e616e38ba70be7ddc8f9610))
* fix broken links and stale references ([01be53e](https://github.com/Leberkas-org/TurboHTTP/commit/01be53e219c5eed699bbbb1d886521fd180d2c54))
* fix code tab layout shift and add emerald/violet two-tone theme ([8d37607](https://github.com/Leberkas-org/TurboHTTP/commit/8d37607d632da573f3ad24e0cddc569d9152a22b))
* **likec4:** clean up server model to match client detail level ([6688f59](https://github.com/Leberkas-org/TurboHTTP/commit/6688f59fe6a282c88127800ba609926b4dd928d1))
* **likec4:** consolidate model-server.c4 into model-pipeline.c4 ([7fdf75e](https://github.com/Leberkas-org/TurboHTTP/commit/7fdf75ef7809243e214841132c150f4faf4b4768))
* make architecture page symmetric between Client and Server ([9964e4d](https://github.com/Leberkas-org/TurboHTTP/commit/9964e4d3c81f7b49ee598a2a409ef7e026f875f5))
* register HomePage and CodeTabs theme components ([eed439f](https://github.com/Leberkas-org/TurboHTTP/commit/eed439f9e6b2403d1bf27fbe6eafdc12c349d49a))
* remove completed planning documents ([ed2d79e](https://github.com/Leberkas-org/TurboHTTP/commit/ed2d79e862507a872536ff50aba0ba259b54a28a))
* remove Extending the Pipeline pages ([16b0196](https://github.com/Leberkas-org/TurboHTTP/commit/16b0196b0219e003c87094e171aa5d3ad86a1203))
* restructure content — overview pages, delete old dirs, add cross-links ([8e409a2](https://github.com/Leberkas-org/TurboHTTP/commit/8e409a27ad2a6a568f3a36186d33c837d6993bf9))
* restructure documentation into client and server sections ([524035d](https://github.com/Leberkas-org/TurboHTTP/commit/524035da0ba38a94d29742511728f5d078b9f72f))
* update ([968cbfe](https://github.com/Leberkas-org/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update CLAUDE.md architecture section for new project structure ([bdee559](https://github.com/Leberkas-org/TurboHTTP/commit/bdee559e2b1b6686c758f342c7be1bc4334ca3be))
* update CLAUDE.md for server project and code style rules ([75b617a](https://github.com/Leberkas-org/TurboHTTP/commit/75b617ae248a8ad4da83ccbe9801a01c99e01e3d))
* Update GitHub repository link ([bc7af3b](https://github.com/Leberkas-org/TurboHTTP/commit/bc7af3bd1f430859026b99776100d8b31f37e604))
* update homepage layout and enhance content page CSS ([3df570e](https://github.com/Leberkas-org/TurboHTTP/commit/3df570e84eb9c20a3a218d81d1effe2274d78e8c))
* update LikeC4 ([478dbb4](https://github.com/Leberkas-org/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))
* update MapTurboEntity examples to new API and fix landing page table ([4f5ef25](https://github.com/Leberkas-org/TurboHTTP/commit/4f5ef257912078c9e39bf4efe22d6ab216eb47db))
* update Obsidian notes ([b933151](https://github.com/Leberkas-org/TurboHTTP/commit/b93315141e1724b67e280f1f5f005584715ad55b))
* Update README with server features ([1f26bb8](https://github.com/Leberkas-org/TurboHTTP/commit/1f26bb80ab21762ae51853ac475a5bd7a22e70b5))
* update VitePress config with new nav and sidebar structure ([33dc752](https://github.com/Leberkas-org/TurboHTTP/commit/33dc752afb118ada31af9e8b346d149693f94b10))


### Dependencies

* Bump actions/checkout from 4 to 6 ([a9a12fa](https://github.com/Leberkas-org/TurboHTTP/commit/a9a12fa54ea75f44dfe00098bb78b0ee71348392))
* bump actions/deploy-pages from 4 to 5 ([6998305](https://github.com/Leberkas-org/TurboHTTP/commit/69983054d11d8ce88462a352b0611a0a0e5dabd9))
* bump actions/setup-node from 4 to 6 ([299470c](https://github.com/Leberkas-org/TurboHTTP/commit/299470c0e773bbf67f43b49b6b577a93125ecb23))
* Bump actions/upload-pages-artifact from 3 to 5 ([233b563](https://github.com/Leberkas-org/TurboHTTP/commit/233b563c65f39d94dc063bd61edb7fccf1c9f9fe))
* Bump Akka.Streams, Akka.Streams.TestKit and Akka.TestKit.Xunit ([b2c3c36](https://github.com/Leberkas-org/TurboHTTP/commit/b2c3c362df304acc44ace4a3023d06da845845b0))
* bump amannn/action-semantic-pull-request from 5 to 6 ([5f3a418](https://github.com/Leberkas-org/TurboHTTP/commit/5f3a4182afbc23598ad7c4065458533250c62d7f))
* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/Leberkas-org/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))
* Bump Microsoft.Testing.Extensions.CodeCoverage from 18.6.2 to 18.7.0 ([6d518f7](https://github.com/Leberkas-org/TurboHTTP/commit/6d518f707c119620bcbb819dfa236c5dd7911445))
* Bump Testcontainers from 4.11.0 to 4.12.0 ([780d25a](https://github.com/Leberkas-org/TurboHTTP/commit/780d25a4b06f79a181e566e5d89688f6394b78cd))
* Bump Testcontainers from 4.6.0 to 4.11.0 ([9d7d0ea](https://github.com/Leberkas-org/TurboHTTP/commit/9d7d0ead5416f0e57e4ff51df8e7b88360934b85))
* Bump Verify.XunitV3 from 31.16.1 to 31.16.3 ([6bfe8bd](https://github.com/Leberkas-org/TurboHTTP/commit/6bfe8bd20359f47dde45b682d368ce4f98cb822c))

## [0.7.1](https://github.com/Leberkas-org/TurboHTTP/compare/v0.7.0...v0.7.1) (2026-05-20)


### Documentation

* align LikeC4 models and docs with actual class names ([75a0a0f](https://github.com/Leberkas-org/TurboHTTP/commit/75a0a0f0e4e5bf7a2233baa96b181b55822a1dd3))
* **likec4:** consolidate model-server.c4 into model-pipeline.c4 ([7fdf75e](https://github.com/Leberkas-org/TurboHTTP/commit/7fdf75ef7809243e214841132c150f4faf4b4768))
* remove Extending the Pipeline pages ([16b0196](https://github.com/Leberkas-org/TurboHTTP/commit/16b0196b0219e003c87094e171aa5d3ad86a1203))
* Update README with server features ([1f26bb8](https://github.com/Leberkas-org/TurboHTTP/commit/1f26bb80ab21762ae51853ac475a5bd7a22e70b5))


### Dependencies

* Bump Akka.Streams, Akka.Streams.TestKit and Akka.TestKit.Xunit ([b2c3c36](https://github.com/Leberkas-org/TurboHTTP/commit/b2c3c362df304acc44ace4a3023d06da845845b0))
* Bump Microsoft.Testing.Extensions.CodeCoverage from 18.6.2 to 18.7.0 ([6d518f7](https://github.com/Leberkas-org/TurboHTTP/commit/6d518f707c119620bcbb819dfa236c5dd7911445))
* Bump Testcontainers from 4.11.0 to 4.12.0 ([780d25a](https://github.com/Leberkas-org/TurboHTTP/commit/780d25a4b06f79a181e566e5d89688f6394b78cd))

## [0.7.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.6.0...v0.7.0) (2026-05-20)


### Features

* **features:** add ITlsHandshakeFeature interface and implementation ([4207222](https://github.com/Leberkas-org/TurboHTTP/commit/42072228166b95f38017a154e6071a277065d75a))
* **http11:** add h2c upgrade detection with IProtocolSwitchCapable signaling ([1e60d31](https://github.com/Leberkas-org/TurboHTTP/commit/1e60d3163e1bbadf92c67491799173c4704a2f4a))
* **lifecycle:** extract TLS metadata ([07fd711](https://github.com/Leberkas-org/TurboHTTP/commit/07fd71114421703629103b74ddbe6639cf20cb15))
* **protocol:** add ProtocolNegotiatingStateMachine with ALPN and preface detection ([befd348](https://github.com/Leberkas-org/TurboHTTP/commit/befd3489bd741ad2412b4df46f62865031d588ea))
* **routing:** add Count property to EntityResponseMapperCollection ([fa65d3d](https://github.com/Leberkas-org/TurboHTTP/commit/fa65d3db0f5bd70bf9a3fa828266e56fb0e7cfac))
* **routing:** extend EntityMethodConfig with endpoint mappers and tell handler ([7891c56](https://github.com/Leberkas-org/TurboHTTP/commit/7891c56d89c0c5eb632be32c1d1e7772456ad433))
* **routing:** map new TLS options in EndpointResolver ([fcc8c6e](https://github.com/Leberkas-org/TurboHTTP/commit/fcc8c6e8d0a774d0faa92cc60e9f6c359b2a2cc2))
* **routing:** update EntityDispatcher with two-tier mapper lookup and pluggable tell handler ([7bda4a1](https://github.com/Leberkas-org/TurboHTTP/commit/7bda4a172f887a75ea614f7cef3cb3d71edb5dfa))
* **server:** add ClientCertificateMode and ServerCertificateSelector to TurboHttpsOptions ([3ba4a8c](https://github.com/Leberkas-org/TurboHTTP/commit/3ba4a8c91fe6ee8ab3e159d36c17dd7df23f8951))
* **server:** add DelayCertificate renegotiation support ([3874559](https://github.com/Leberkas-org/TurboHTTP/commit/3874559468e42b8edf027c08757d86a340a69098))
* **server:** add IsAsk/IsTell to TurboEntityMethodBuilder, deprecate AcceptedResponse ([ce010a3](https://github.com/Leberkas-org/TurboHTTP/commit/ce010a3c732b724c3ca5d7025896f7dcc935c72b))
* **server:** add TurboEntityAskBuilder with Response, Produces, and WithTimeout support ([a093d79](https://github.com/Leberkas-org/TurboHTTP/commit/a093d79809f6a6e648083cd46e618ac893dc02b8))
* **server:** add TurboEntityTellBuilder with Response and Produces support ([e069149](https://github.com/Leberkas-org/TurboHTTP/commit/e069149b187059798ec58402c6ec8c98169a1483))
* **server:** add TurboTlsCallbackOptions and TurboTlsCallbackContext ([a1ddcf7](https://github.com/Leberkas-org/TurboHTTP/commit/a1ddcf76e16571266dc3f87a66d4db56fd1afab5))
* **server:** add UseHttps(TurboTlsCallbackOptions) overload to TurboListenOptions ([1217894](https://github.com/Leberkas-org/TurboHTTP/commit/1217894ae8fcbaebc195ad9a84c40cce4a6489cd))
* **server:** populate ITlsHandshakeFeature on HttpContext feature ([f50dd4c](https://github.com/Leberkas-org/TurboHTTP/commit/f50dd4c84be3f13c4ab8478c4ee7216e70b5f3db))
* **streams:** add NegotiatingServerEngine ([1b01747](https://github.com/Leberkas-org/TurboHTTP/commit/1b017470ebabea0812fca9db04c2925c6bdfd03d))
* **transport:** add ClientCertificateMode enum ([06de0c0](https://github.com/Leberkas-org/TurboHTTP/commit/06de0c0532c75d76130e74539f815b409958af0c))
* **transport:** add ClientCertificateMode, HandshakeCallback, ServerCertificateSelector ([a98c9be](https://github.com/Leberkas-org/TurboHTTP/commit/a98c9be99a7d3885b2a6de2402c09c75bd6bcdae))
* **transport:** add TlsHandshakeContext, TlsHandshakeCallback, TlsConnectionResult ([3ba8d70](https://github.com/Leberkas-org/TurboHTTP/commit/3ba8d70b6c1490d0b53213de608fa3313b6875bd))
* **transport:** add TransportTlsState inbound message for DelayCertificate ([98ee596](https://github.com/Leberkas-org/TurboHTTP/commit/98ee596603fe70f2ac053ab0518ac01834e5d070))
* **transport:** extend SecurityInfo with NegotiatedCipherSuite and HostName ([d38c319](https://github.com/Leberkas-org/TurboHTTP/commit/d38c319ac4ba442125798481ccdb2064664b25d4))
* **transport:** rewrite TcpListenerStage handshake with 3 paths ([a84d189](https://github.com/Leberkas-org/TurboHTTP/commit/a84d1898cbe35f098bac7f891d1b64833baefd81))


### Documentation

* add dynamic protocol negotiation design spec ([ab58122](https://github.com/Leberkas-org/TurboHTTP/commit/ab58122bb80d5251958399019bf06fe133dbc420))
* add dynamic protocol negotiation implementation plan ([45ba63e](https://github.com/Leberkas-org/TurboHTTP/commit/45ba63eb9c5ad84b2901cddd0d6bb311de820c48))

## [0.6.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.5.0...v0.6.0) (2026-05-20)


### Features

* add generic HttpConnectionStageLogic&lt;TSM&gt; base stage ([4901d10](https://github.com/Leberkas-org/TurboHTTP/commit/4901d1004a5fb6e662c94c1c0d9032a9d9b9c05b))
* add pipeline tracing across all BidiStages and stage logic ([3d31bbe](https://github.com/Leberkas-org/TurboHTTP/commit/3d31bbe540d02d6551e13d113ce49afadf0ace92))
* add QUIC transport implementation ([780d0c0](https://github.com/Leberkas-org/TurboHTTP/commit/780d0c0c6e3c685c95b4be29105053c3e366cbd7))
* add RequestFault helper for shared request error handling ([5c7f491](https://github.com/Leberkas-org/TurboHTTP/commit/5c7f491e5af5c2d56f2d2f8c9f30114c58811a52))
* add Servus.Akka.TestKit dependency ([98bffd1](https://github.com/Leberkas-org/TurboHTTP/commit/98bffd190ad144fb30682ac5bfad143724978db6))
* add Servus.Akka.Transport listener ([70ad8bf](https://github.com/Leberkas-org/TurboHTTP/commit/70ad8bf99cfffde2f09148cfc3c8bf59df6aa613))
* add ServusTrace integration ([722ea70](https://github.com/Leberkas-org/TurboHTTP/commit/722ea7047977854b8b9cd361e0f22bc580ea8b87))
* add TCP transport implementation ([ebf6689](https://github.com/Leberkas-org/TurboHTTP/commit/ebf66890bb54ab84565b32456b516f64b449606e))
* **bench:** add microbenchmark project with baseline comparisons ([3d5a6b0](https://github.com/Leberkas-org/TurboHTTP/commit/3d5a6b0ae635ffb3bdb6d91d9c249147181f383c))
* define IHttpStateMachine interface and expand IStageOperations ([6876159](https://github.com/Leberkas-org/TurboHTTP/commit/68761591d4a763b9d9c3448ad103fda09edb4ccc))
* **diagnostics:** add HexDumpFormatter for Kestrel-style wire dumps ([cbcfb5a](https://github.com/Leberkas-org/TurboHTTP/commit/cbcfb5af406764faeaf80df02b8bbb042c0401f7))
* **h11:** implement IHttpStateMachine on Http11 StateMachine ([5420762](https://github.com/Leberkas-org/TurboHTTP/commit/5420762806eba3f4b22bff8ed71599af19c20284))
* **h2:** wire InitialStreamWindowSize into server SETTINGS frame ([861a7c4](https://github.com/Leberkas-org/TurboHTTP/commit/861a7c4988a8bcb1862ebee6552ae0f2dedc2d12))
* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/Leberkas-org/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/Leberkas-org/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/Leberkas-org/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/Leberkas-org/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/Leberkas-org/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/Leberkas-org/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/Leberkas-org/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/Leberkas-org/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/Leberkas-org/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/Leberkas-org/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))
* **lifecycle:** add ConsumerActor for per-consumer ingress and response sink ([b135cd3](https://github.com/Leberkas-org/TurboHTTP/commit/b135cd386d7484cfb2b379840282bd913a95c542))
* **protocol:** extract encoder/decoder options for HTTP/2 and HTTP/3 ([138b68a](https://github.com/Leberkas-org/TurboHTTP/commit/138b68aed7b16ffc6b9706857a43b6461b04b3dd))
* **semantics:** add RFC-compliant HTTP semantic validators ([8566489](https://github.com/Leberkas-org/TurboHTTP/commit/8566489631ec8e4d4de0fc02252fce8b9188d90e))
* **server:** add ConnectionLoggingBidiStage for wire-level hex dump logging ([02dc33c](https://github.com/Leberkas-org/TurboHTTP/commit/02dc33cdc3e26c3a728b2b02a6e816eac1f74f3f))
* **server:** add UseConnectionLogging() and wire through to ConnectionActor ([b46eb6f](https://github.com/Leberkas-org/TurboHTTP/commit/b46eb6f7cd028fc27bd247eb4dffbe57c3042268))
* **server:** enforce MaxConcurrentConnections per listener ([eaa333a](https://github.com/Leberkas-org/TurboHTTP/commit/eaa333abb9818286ec9dbca521d79e942eeb396d))
* **server:** implement entity gateway with ASP.NET-style middleware pipeline ([04d4b50](https://github.com/Leberkas-org/TurboHTTP/commit/04d4b5013c20d6b1cf5ae6555218532ed4cfaf81))
* **server:** Inject IMaterializer into HttpContext ([a01f5bf](https://github.com/Leberkas-org/TurboHTTP/commit/a01f5bf853822caecd0c3cdde5e23b14a4a55a96))
* **server:** split Http2ServerOptions.InitialWindowSize into connection and stream properties ([e19191a](https://github.com/Leberkas-org/TurboHTTP/commit/e19191ad96d9a85169eca599b952eb1723a097ae))
* **streams:** add Pipe stages for bidirectional IO bridging ([9865763](https://github.com/Leberkas-org/TurboHTTP/commit/9865763a79ff4560f7dc1022139ca4d432fa96a3))
* **tests:** configure xunit parallelization and timeouts ([143003f](https://github.com/Leberkas-org/TurboHTTP/commit/143003f207ae992e8c94e1ea8d32b48f3597c234))
* **tests:** Migrate integration tests to new structure ([f981f7a](https://github.com/Leberkas-org/TurboHTTP/commit/f981f7abb306d65b48c91ebae2395351d3d9d1f4))
* **TurboHTTP.Server:** add Akka.Streams-based HTTP server ([2cab2cf](https://github.com/Leberkas-org/TurboHTTP/commit/2cab2cfe0705f24b162ace599dedab3834418929))
* **vault:** convert all 393 RFC section files to VAULT_STYLE_GUIDE-compliant Markdown ([b9c3a81](https://github.com/Leberkas-org/TurboHTTP/commit/b9c3a81cbab00b7b73d65b2737a8a7cf29bcd12b))


### Bug Fixes

* add exception safety to all pipeline onPush handlers ([7e481e4](https://github.com/Leberkas-org/TurboHTTP/commit/7e481e4bf0fe5418867b7d88d811764a2f082c03))
* align test expectations with corrected server option defaults ([ced7837](https://github.com/Leberkas-org/TurboHTTP/commit/ced7837300852b53174c885784bfc29120785c9d))
* checkout with lfs ([7a470ab](https://github.com/Leberkas-org/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **ci:** adjust release manifest directory ([44980a5](https://github.com/Leberkas-org/TurboHTTP/commit/44980a5bc10fec9ab44656b037e3ed621d14e6dd))
* **ci:** integrate deps commit type ([5f88452](https://github.com/Leberkas-org/TurboHTTP/commit/5f884529c0df9fa64889ea2ae38c42b0fd27a631))
* **commitlint:** ignore dependabot commits ([f3a662a](https://github.com/Leberkas-org/TurboHTTP/commit/f3a662aced507f71057ba1d88416c018cbe42e88))
* EntityDispatcher tests ([e89a3f2](https://github.com/Leberkas-org/TurboHTTP/commit/e89a3f2f34d74933dbe72dc1e87af1458081ddca))
* fix namespace errors ([bc5d0f9](https://github.com/Leberkas-org/TurboHTTP/commit/bc5d0f91e29e6b5e73b859f03af3fe4192313a54))
* **h11:** handle reconnect failure gracefully instead of failing stage ([dc6066a](https://github.com/Leberkas-org/TurboHTTP/commit/dc6066ae9058c0ea7c1b1a3867392dc472927e4d))
* **h11:** use OnComplete for reconnect exhaustion instead of OnFail ([eafefdd](https://github.com/Leberkas-org/TurboHTTP/commit/eafefdd0bd82e521523a63a0d024f7621990bbea))
* **h3:** improve control stream stability ([153a37b](https://github.com/Leberkas-org/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/Leberkas-org/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/Leberkas-org/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/Leberkas-org/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* minor fixes ([2d62179](https://github.com/Leberkas-org/TurboHTTP/commit/2d62179e9c8a91c4591f50d33bfb3b39ff8921bc))
* minor fixes ([f1cb795](https://github.com/Leberkas-org/TurboHTTP/commit/f1cb79575a48d17e6ac6f3392640142662c3b6bf))
* minor transport fix ([9b1bba2](https://github.com/Leberkas-org/TurboHTTP/commit/9b1bba223aeffc63b87dd63db3714c4d54a53e80))
* obsolete ctor ([acffe6c](https://github.com/Leberkas-org/TurboHTTP/commit/acffe6cc87d339e04765f5b73393c28caf1b14d8))
* public api changes ([2d6dfa4](https://github.com/Leberkas-org/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/Leberkas-org/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))
* **readme:** Correct workflow badges ([1392385](https://github.com/Leberkas-org/TurboHTTP/commit/13923855b98ce487754d43baac42b13bdd720c32))
* release please ([c1c7ae3](https://github.com/Leberkas-org/TurboHTTP/commit/c1c7ae30a1841782fe2440d2c2353747514b56d7))
* **release:** correct config paths ([a6ad77e](https://github.com/Leberkas-org/TurboHTTP/commit/a6ad77ee7bae28faa1515ac9cd7562ece4731cff))
* resolve H11 POST redirect deadlock and enable skipped acceptance tests ([f5e0564](https://github.com/Leberkas-org/TurboHTTP/commit/f5e05649728fa5bf36b521125bdbbe2fb077f050))
* **security:** address CodeQL findings for cookie injection and open redirect ([6bfb4ee](https://github.com/Leberkas-org/TurboHTTP/commit/6bfb4ee87ac4f133d2c4cc4ec2c64f6a69647971))
* **security:** prevent open redirect in HandleRedirectTo and harden Redirect() ([57b0b88](https://github.com/Leberkas-org/TurboHTTP/commit/57b0b885ddda319b7e5cd9b821ebeaa766912360))
* **security:** reject CRLF and normalize URLs in TurboHttpResponse.Redirect() ([81e1aa9](https://github.com/Leberkas-org/TurboHTTP/commit/81e1aa9f56e04fcec5bbba4b9aa5d68b891ed22d))
* **security:** sanitize response header values in test httpbin endpoint ([5a34979](https://github.com/Leberkas-org/TurboHTTP/commit/5a3497994cbf12f134d91396dc7224502711cb53))
* **Servus.Akka:** use async dispose and cancellation ([ac318f4](https://github.com/Leberkas-org/TurboHTTP/commit/ac318f4ad3939acfb76f9a3f3e142b87de7d40b1))
* **test:** generate HTTPS test certificate programmatically ([7d5a954](https://github.com/Leberkas-org/TurboHTTP/commit/7d5a95464b8a82d79c5414d37c9a8f53f3e6be74))
* **transport:** make TLS handshake async with configurable timeout ([92bc679](https://github.com/Leberkas-org/TurboHTTP/commit/92bc679bf74ed0afc2cde349f5f31961f4978c75))
* wire TurboHttpRequest.BodySource to ITurboRequestBodyFeature ([132a30d](https://github.com/Leberkas-org/TurboHTTP/commit/132a30d903df31277890dca088d5c6c2d5259180))


### Performance

* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/Leberkas-org/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* enhance HTTP/2 and HTTP/3 transport performance and streaming ([74d0b30](https://github.com/Leberkas-org/TurboHTTP/commit/74d0b30d7105482d1933354c09ea6aedd9cfd5f3))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/Leberkas-org/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/Leberkas-org/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/Leberkas-org/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/Leberkas-org/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/Leberkas-org/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/Leberkas-org/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))


### Documentation

* add Akka.Streams integration scenario for client ([357ce16](https://github.com/Leberkas-org/TurboHTTP/commit/357ce1697825c5cc1fbae2fbcda5dbe627d78095))
* add Architecture as top-level nav with Client/Server groups ([6a9866a](https://github.com/Leberkas-org/TurboHTTP/commit/6a9866a3ba4b0f7ba35cb36e6f6288f54eb5b4ff))
* add eager pipeline materialization spec and plan ([a77a1f4](https://github.com/Leberkas-org/TurboHTTP/commit/a77a1f434153fc7056a25d13f2d0fb93624468e8))
* add HomePage and CodeTabs Vue components ([909d3b7](https://github.com/Leberkas-org/TurboHTTP/commit/909d3b7b948c3d3363dd3bb3b99f3e5fc13c726c))
* add LikeC4 server architecture diagrams ([10b16a5](https://github.com/Leberkas-org/TurboHTTP/commit/10b16a5163bcbe50d4b3155d348f7a8dc748d4c7))
* add planning documents and update CLAUDE.md ([a3c59d1](https://github.com/Leberkas-org/TurboHTTP/commit/a3c59d1b4a3d6b981316799a72031c1d74905b5b))
* add real-world client scenario examples ([0a4dab7](https://github.com/Leberkas-org/TurboHTTP/commit/0a4dab7fa9d6b9e861147b169325bfd9ce2f1960))
* add real-world server scenario examples ([bd68625](https://github.com/Leberkas-org/TurboHTTP/commit/bd6862575bc6cb677e8707c682c7ec51197a706a))
* add StreamTests consolidation and microbenchmark plans ([6cbcf00](https://github.com/Leberkas-org/TurboHTTP/commit/6cbcf004fad93b74ca4ffd57599127de3d476e79))
* add symmetric server architecture pages (pipeline, engines, extending) ([34dc02a](https://github.com/Leberkas-org/TurboHTTP/commit/34dc02abf22912494e3ec5d06752c16d961d2989))
* add test restructuring spec ([a8c4dd2](https://github.com/Leberkas-org/TurboHTTP/commit/a8c4dd2fce3df4d49eaf1463bbfddf82b7f55644))
* add VitePress redesign implementation plan ([bb535e5](https://github.com/Leberkas-org/TurboHTTP/commit/bb535e5c67e8a72deb8e494f36e4e0d262f5f014))
* add VitePress redesign spec ([390a611](https://github.com/Leberkas-org/TurboHTTP/commit/390a611997139e2ba0ee13299fb3156ed14bedcf))
* complete API reference split (server, entity gateway, overview) ([d9a7471](https://github.com/Leberkas-org/TurboHTTP/commit/d9a747165870436f3f3e0ed376fd7478e9fdf927))
* correct server narrative — standalone server, not Kestrel-basedr ([3aa50bb](https://github.com/Leberkas-org/TurboHTTP/commit/3aa50bbbf595226090f1b28ba30444befb5624d6))
* create Getting Started section with quick starts and architecture overview ([320afd0](https://github.com/Leberkas-org/TurboHTTP/commit/320afd0ee92db821e303171940e0c8b51cb51606))
* create split API reference pages (client, options, features) ([423a479](https://github.com/Leberkas-org/TurboHTTP/commit/423a479d75fdf2dc9e616e38ba70be7ddc8f9610))
* fix broken links and stale references ([01be53e](https://github.com/Leberkas-org/TurboHTTP/commit/01be53e219c5eed699bbbb1d886521fd180d2c54))
* fix code tab layout shift and add emerald/violet two-tone theme ([8d37607](https://github.com/Leberkas-org/TurboHTTP/commit/8d37607d632da573f3ad24e0cddc569d9152a22b))
* **likec4:** clean up server model to match client detail level ([6688f59](https://github.com/Leberkas-org/TurboHTTP/commit/6688f59fe6a282c88127800ba609926b4dd928d1))
* make architecture page symmetric between Client and Server ([9964e4d](https://github.com/Leberkas-org/TurboHTTP/commit/9964e4d3c81f7b49ee598a2a409ef7e026f875f5))
* register HomePage and CodeTabs theme components ([eed439f](https://github.com/Leberkas-org/TurboHTTP/commit/eed439f9e6b2403d1bf27fbe6eafdc12c349d49a))
* remove completed planning documents ([ed2d79e](https://github.com/Leberkas-org/TurboHTTP/commit/ed2d79e862507a872536ff50aba0ba259b54a28a))
* restructure content — overview pages, delete old dirs, add cross-links ([8e409a2](https://github.com/Leberkas-org/TurboHTTP/commit/8e409a27ad2a6a568f3a36186d33c837d6993bf9))
* restructure documentation into client and server sections ([524035d](https://github.com/Leberkas-org/TurboHTTP/commit/524035da0ba38a94d29742511728f5d078b9f72f))
* update ([968cbfe](https://github.com/Leberkas-org/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update CLAUDE.md architecture section for new project structure ([bdee559](https://github.com/Leberkas-org/TurboHTTP/commit/bdee559e2b1b6686c758f342c7be1bc4334ca3be))
* update CLAUDE.md for server project and code style rules ([75b617a](https://github.com/Leberkas-org/TurboHTTP/commit/75b617ae248a8ad4da83ccbe9801a01c99e01e3d))
* Update GitHub repository link ([bc7af3b](https://github.com/Leberkas-org/TurboHTTP/commit/bc7af3bd1f430859026b99776100d8b31f37e604))
* update homepage layout and enhance content page CSS ([3df570e](https://github.com/Leberkas-org/TurboHTTP/commit/3df570e84eb9c20a3a218d81d1effe2274d78e8c))
* update LikeC4 ([478dbb4](https://github.com/Leberkas-org/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))
* update MapTurboEntity examples to new API and fix landing page table ([4f5ef25](https://github.com/Leberkas-org/TurboHTTP/commit/4f5ef257912078c9e39bf4efe22d6ab216eb47db))
* update Obsidian notes ([b933151](https://github.com/Leberkas-org/TurboHTTP/commit/b93315141e1724b67e280f1f5f005584715ad55b))
* update VitePress config with new nav and sidebar structure ([33dc752](https://github.com/Leberkas-org/TurboHTTP/commit/33dc752afb118ada31af9e8b346d149693f94b10))


### Dependencies

* Bump actions/checkout from 4 to 6 ([a9a12fa](https://github.com/Leberkas-org/TurboHTTP/commit/a9a12fa54ea75f44dfe00098bb78b0ee71348392))
* bump actions/deploy-pages from 4 to 5 ([6998305](https://github.com/Leberkas-org/TurboHTTP/commit/69983054d11d8ce88462a352b0611a0a0e5dabd9))
* bump actions/setup-node from 4 to 6 ([299470c](https://github.com/Leberkas-org/TurboHTTP/commit/299470c0e773bbf67f43b49b6b577a93125ecb23))
* Bump actions/upload-pages-artifact from 3 to 5 ([233b563](https://github.com/Leberkas-org/TurboHTTP/commit/233b563c65f39d94dc063bd61edb7fccf1c9f9fe))
* bump amannn/action-semantic-pull-request from 5 to 6 ([5f3a418](https://github.com/Leberkas-org/TurboHTTP/commit/5f3a4182afbc23598ad7c4065458533250c62d7f))
* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/Leberkas-org/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))
* Bump Testcontainers from 4.6.0 to 4.11.0 ([9d7d0ea](https://github.com/Leberkas-org/TurboHTTP/commit/9d7d0ead5416f0e57e4ff51df8e7b88360934b85))
* Bump Verify.XunitV3 from 31.16.1 to 31.16.3 ([6bfe8bd](https://github.com/Leberkas-org/TurboHTTP/commit/6bfe8bd20359f47dde45b682d368ce4f98cb822c))

## [0.5.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.4.0...v0.5.0) (2026-05-20)


### Features

* **diagnostics:** add HexDumpFormatter for Kestrel-style wire dumps ([cbcfb5a](https://github.com/Leberkas-org/TurboHTTP/commit/cbcfb5af406764faeaf80df02b8bbb042c0401f7))
* **h2:** wire InitialStreamWindowSize into server SETTINGS frame ([861a7c4](https://github.com/Leberkas-org/TurboHTTP/commit/861a7c4988a8bcb1862ebee6552ae0f2dedc2d12))
* **server:** add ConnectionLoggingBidiStage for wire-level hex dump logging ([02dc33c](https://github.com/Leberkas-org/TurboHTTP/commit/02dc33cdc3e26c3a728b2b02a6e816eac1f74f3f))
* **server:** add UseConnectionLogging() and wire through to ConnectionActor ([b46eb6f](https://github.com/Leberkas-org/TurboHTTP/commit/b46eb6f7cd028fc27bd247eb4dffbe57c3042268))
* **server:** enforce MaxConcurrentConnections per listener ([eaa333a](https://github.com/Leberkas-org/TurboHTTP/commit/eaa333abb9818286ec9dbca521d79e942eeb396d))
* **server:** split Http2ServerOptions.InitialWindowSize into connection and stream properties ([e19191a](https://github.com/Leberkas-org/TurboHTTP/commit/e19191ad96d9a85169eca599b952eb1723a097ae))


### Bug Fixes

* align test expectations with corrected server option defaults ([ced7837](https://github.com/Leberkas-org/TurboHTTP/commit/ced7837300852b53174c885784bfc29120785c9d))


### Documentation

* add Akka.Streams integration scenario for client ([357ce16](https://github.com/Leberkas-org/TurboHTTP/commit/357ce1697825c5cc1fbae2fbcda5dbe627d78095))
* add Architecture as top-level nav with Client/Server groups ([6a9866a](https://github.com/Leberkas-org/TurboHTTP/commit/6a9866a3ba4b0f7ba35cb36e6f6288f54eb5b4ff))
* add HomePage and CodeTabs Vue components ([909d3b7](https://github.com/Leberkas-org/TurboHTTP/commit/909d3b7b948c3d3363dd3bb3b99f3e5fc13c726c))
* add LikeC4 server architecture diagrams ([10b16a5](https://github.com/Leberkas-org/TurboHTTP/commit/10b16a5163bcbe50d4b3155d348f7a8dc748d4c7))
* add real-world client scenario examples ([0a4dab7](https://github.com/Leberkas-org/TurboHTTP/commit/0a4dab7fa9d6b9e861147b169325bfd9ce2f1960))
* add real-world server scenario examples ([bd68625](https://github.com/Leberkas-org/TurboHTTP/commit/bd6862575bc6cb677e8707c682c7ec51197a706a))
* add symmetric server architecture pages (pipeline, engines, extending) ([34dc02a](https://github.com/Leberkas-org/TurboHTTP/commit/34dc02abf22912494e3ec5d06752c16d961d2989))
* add test restructuring spec ([a8c4dd2](https://github.com/Leberkas-org/TurboHTTP/commit/a8c4dd2fce3df4d49eaf1463bbfddf82b7f55644))
* add VitePress redesign implementation plan ([bb535e5](https://github.com/Leberkas-org/TurboHTTP/commit/bb535e5c67e8a72deb8e494f36e4e0d262f5f014))
* add VitePress redesign spec ([390a611](https://github.com/Leberkas-org/TurboHTTP/commit/390a611997139e2ba0ee13299fb3156ed14bedcf))
* complete API reference split (server, entity gateway, overview) ([d9a7471](https://github.com/Leberkas-org/TurboHTTP/commit/d9a747165870436f3f3e0ed376fd7478e9fdf927))
* correct server narrative — standalone server, not Kestrel-basedr ([3aa50bb](https://github.com/Leberkas-org/TurboHTTP/commit/3aa50bbbf595226090f1b28ba30444befb5624d6))
* create Getting Started section with quick starts and architecture overview ([320afd0](https://github.com/Leberkas-org/TurboHTTP/commit/320afd0ee92db821e303171940e0c8b51cb51606))
* create split API reference pages (client, options, features) ([423a479](https://github.com/Leberkas-org/TurboHTTP/commit/423a479d75fdf2dc9e616e38ba70be7ddc8f9610))
* fix broken links and stale references ([01be53e](https://github.com/Leberkas-org/TurboHTTP/commit/01be53e219c5eed699bbbb1d886521fd180d2c54))
* fix code tab layout shift and add emerald/violet two-tone theme ([8d37607](https://github.com/Leberkas-org/TurboHTTP/commit/8d37607d632da573f3ad24e0cddc569d9152a22b))
* **likec4:** clean up server model to match client detail level ([6688f59](https://github.com/Leberkas-org/TurboHTTP/commit/6688f59fe6a282c88127800ba609926b4dd928d1))
* make architecture page symmetric between Client and Server ([9964e4d](https://github.com/Leberkas-org/TurboHTTP/commit/9964e4d3c81f7b49ee598a2a409ef7e026f875f5))
* register HomePage and CodeTabs theme components ([eed439f](https://github.com/Leberkas-org/TurboHTTP/commit/eed439f9e6b2403d1bf27fbe6eafdc12c349d49a))
* restructure content — overview pages, delete old dirs, add cross-links ([8e409a2](https://github.com/Leberkas-org/TurboHTTP/commit/8e409a27ad2a6a568f3a36186d33c837d6993bf9))
* update CLAUDE.md architecture section for new project structure ([bdee559](https://github.com/Leberkas-org/TurboHTTP/commit/bdee559e2b1b6686c758f342c7be1bc4334ca3be))
* update homepage layout and enhance content page CSS ([3df570e](https://github.com/Leberkas-org/TurboHTTP/commit/3df570e84eb9c20a3a218d81d1effe2274d78e8c))
* update MapTurboEntity examples to new API and fix landing page table ([4f5ef25](https://github.com/Leberkas-org/TurboHTTP/commit/4f5ef257912078c9e39bf4efe22d6ab216eb47db))
* update VitePress config with new nav and sidebar structure ([33dc752](https://github.com/Leberkas-org/TurboHTTP/commit/33dc752afb118ada31af9e8b346d149693f94b10))

## [0.4.0](https://github.com/Leberkas-org/TurboHTTP/compare/v0.3.0...v0.4.0) (2026-05-19)


### Features

* add generic HttpConnectionStageLogic&lt;TSM&gt; base stage ([4901d10](https://github.com/Leberkas-org/TurboHTTP/commit/4901d1004a5fb6e662c94c1c0d9032a9d9b9c05b))
* add pipeline tracing across all BidiStages and stage logic ([3d31bbe](https://github.com/Leberkas-org/TurboHTTP/commit/3d31bbe540d02d6551e13d113ce49afadf0ace92))
* add RequestFault helper for shared request error handling ([5c7f491](https://github.com/Leberkas-org/TurboHTTP/commit/5c7f491e5af5c2d56f2d2f8c9f30114c58811a52))
* **bench:** add microbenchmark project with baseline comparisons ([3d5a6b0](https://github.com/Leberkas-org/TurboHTTP/commit/3d5a6b0ae635ffb3bdb6d91d9c249147181f383c))
* define IHttpStateMachine interface and expand IStageOperations ([6876159](https://github.com/Leberkas-org/TurboHTTP/commit/68761591d4a763b9d9c3448ad103fda09edb4ccc))
* **h11:** implement IHttpStateMachine on Http11 StateMachine ([5420762](https://github.com/Leberkas-org/TurboHTTP/commit/5420762806eba3f4b22bff8ed71599af19c20284))
* **lifecycle:** add ConsumerActor for per-consumer ingress and response sink ([b135cd3](https://github.com/Leberkas-org/TurboHTTP/commit/b135cd386d7484cfb2b379840282bd913a95c542))
* **protocol:** extract encoder/decoder options for HTTP/2 and HTTP/3 ([138b68a](https://github.com/Leberkas-org/TurboHTTP/commit/138b68aed7b16ffc6b9706857a43b6461b04b3dd))
* **semantics:** add RFC-compliant HTTP semantic validators ([8566489](https://github.com/Leberkas-org/TurboHTTP/commit/8566489631ec8e4d4de0fc02252fce8b9188d90e))
* **server:** implement entity gateway with ASP.NET-style middleware pipeline ([04d4b50](https://github.com/Leberkas-org/TurboHTTP/commit/04d4b5013c20d6b1cf5ae6555218532ed4cfaf81))
* **server:** Inject IMaterializer into HttpContext ([a01f5bf](https://github.com/Leberkas-org/TurboHTTP/commit/a01f5bf853822caecd0c3cdde5e23b14a4a55a96))
* **streams:** add Pipe stages for bidirectional IO bridging ([9865763](https://github.com/Leberkas-org/TurboHTTP/commit/9865763a79ff4560f7dc1022139ca4d432fa96a3))
* **tests:** configure xunit parallelization and timeouts ([143003f](https://github.com/Leberkas-org/TurboHTTP/commit/143003f207ae992e8c94e1ea8d32b48f3597c234))
* **tests:** Migrate integration tests to new structure ([f981f7a](https://github.com/Leberkas-org/TurboHTTP/commit/f981f7abb306d65b48c91ebae2395351d3d9d1f4))
* **TurboHTTP.Server:** add Akka.Streams-based HTTP server ([2cab2cf](https://github.com/Leberkas-org/TurboHTTP/commit/2cab2cfe0705f24b162ace599dedab3834418929))


### Bug Fixes

* add exception safety to all pipeline onPush handlers ([7e481e4](https://github.com/Leberkas-org/TurboHTTP/commit/7e481e4bf0fe5418867b7d88d811764a2f082c03))
* EntityDispatcher tests ([e89a3f2](https://github.com/Leberkas-org/TurboHTTP/commit/e89a3f2f34d74933dbe72dc1e87af1458081ddca))
* fix namespace errors ([bc5d0f9](https://github.com/Leberkas-org/TurboHTTP/commit/bc5d0f91e29e6b5e73b859f03af3fe4192313a54))
* **h11:** handle reconnect failure gracefully instead of failing stage ([dc6066a](https://github.com/Leberkas-org/TurboHTTP/commit/dc6066ae9058c0ea7c1b1a3867392dc472927e4d))
* **h11:** use OnComplete for reconnect exhaustion instead of OnFail ([eafefdd](https://github.com/Leberkas-org/TurboHTTP/commit/eafefdd0bd82e521523a63a0d024f7621990bbea))
* obsolete ctor ([acffe6c](https://github.com/Leberkas-org/TurboHTTP/commit/acffe6cc87d339e04765f5b73393c28caf1b14d8))
* resolve H11 POST redirect deadlock and enable skipped acceptance tests ([f5e0564](https://github.com/Leberkas-org/TurboHTTP/commit/f5e05649728fa5bf36b521125bdbbe2fb077f050))
* **security:** address CodeQL findings for cookie injection and open redirect ([6bfb4ee](https://github.com/Leberkas-org/TurboHTTP/commit/6bfb4ee87ac4f133d2c4cc4ec2c64f6a69647971))
* **security:** prevent open redirect in HandleRedirectTo and harden Redirect() ([57b0b88](https://github.com/Leberkas-org/TurboHTTP/commit/57b0b885ddda319b7e5cd9b821ebeaa766912360))
* **security:** reject CRLF and normalize URLs in TurboHttpResponse.Redirect() ([81e1aa9](https://github.com/Leberkas-org/TurboHTTP/commit/81e1aa9f56e04fcec5bbba4b9aa5d68b891ed22d))
* **security:** sanitize response header values in test httpbin endpoint ([5a34979](https://github.com/Leberkas-org/TurboHTTP/commit/5a3497994cbf12f134d91396dc7224502711cb53))
* **Servus.Akka:** use async dispose and cancellation ([ac318f4](https://github.com/Leberkas-org/TurboHTTP/commit/ac318f4ad3939acfb76f9a3f3e142b87de7d40b1))
* **test:** generate HTTPS test certificate programmatically ([7d5a954](https://github.com/Leberkas-org/TurboHTTP/commit/7d5a95464b8a82d79c5414d37c9a8f53f3e6be74))
* **transport:** make TLS handshake async with configurable timeout ([92bc679](https://github.com/Leberkas-org/TurboHTTP/commit/92bc679bf74ed0afc2cde349f5f31961f4978c75))
* wire TurboHttpRequest.BodySource to ITurboRequestBodyFeature ([132a30d](https://github.com/Leberkas-org/TurboHTTP/commit/132a30d903df31277890dca088d5c6c2d5259180))


### Documentation

* add eager pipeline materialization spec and plan ([a77a1f4](https://github.com/Leberkas-org/TurboHTTP/commit/a77a1f434153fc7056a25d13f2d0fb93624468e8))
* add planning documents and update CLAUDE.md ([a3c59d1](https://github.com/Leberkas-org/TurboHTTP/commit/a3c59d1b4a3d6b981316799a72031c1d74905b5b))
* add StreamTests consolidation and microbenchmark plans ([6cbcf00](https://github.com/Leberkas-org/TurboHTTP/commit/6cbcf004fad93b74ca4ffd57599127de3d476e79))
* remove completed planning documents ([ed2d79e](https://github.com/Leberkas-org/TurboHTTP/commit/ed2d79e862507a872536ff50aba0ba259b54a28a))
* restructure documentation into client and server sections ([524035d](https://github.com/Leberkas-org/TurboHTTP/commit/524035da0ba38a94d29742511728f5d078b9f72f))
* update CLAUDE.md for server project and code style rules ([75b617a](https://github.com/Leberkas-org/TurboHTTP/commit/75b617ae248a8ad4da83ccbe9801a01c99e01e3d))
* Update GitHub repository link ([bc7af3b](https://github.com/Leberkas-org/TurboHTTP/commit/bc7af3bd1f430859026b99776100d8b31f37e604))


### Dependencies

* Bump Testcontainers from 4.6.0 to 4.11.0 ([9d7d0ea](https://github.com/Leberkas-org/TurboHTTP/commit/9d7d0ead5416f0e57e4ff51df8e7b88360934b85))
* Bump Verify.XunitV3 from 31.16.1 to 31.16.3 ([6bfe8bd](https://github.com/Leberkas-org/TurboHTTP/commit/6bfe8bd20359f47dde45b682d368ce4f98cb822c))

## [0.3.0](https://github.com/st0o0/TurboHTTP/compare/v0.2.0...v0.3.0) (2026-05-07)


### Features

* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/st0o0/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/st0o0/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/st0o0/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/st0o0/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/st0o0/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/st0o0/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/st0o0/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/st0o0/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/st0o0/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/st0o0/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))


### Bug Fixes

* checkout with lfs ([7a470ab](https://github.com/st0o0/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **h3:** improve control stream stability ([153a37b](https://github.com/st0o0/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/st0o0/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/st0o0/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/st0o0/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* public api changes ([2d6dfa4](https://github.com/st0o0/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/st0o0/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))
* release please ([c1c7ae3](https://github.com/st0o0/TurboHTTP/commit/c1c7ae30a1841782fe2440d2c2353747514b56d7))


### Performance

* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/st0o0/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/st0o0/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/st0o0/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/st0o0/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/st0o0/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/st0o0/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/st0o0/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))


### Documentation

* update ([968cbfe](https://github.com/st0o0/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update LikeC4 ([478dbb4](https://github.com/st0o0/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))


### Dependencies

* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/st0o0/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))

## [0.3.0](https://github.com/st0o0/TurboHTTP/compare/v0.2.0...v0.3.0) (2026-05-07)


### Features

* **h3:** Activate QPACK dynamic table ([caac8a1](https://github.com/st0o0/TurboHTTP/commit/caac8a171817fb2561a635280862a722d077232a))
* **h3:** Add MaxConcurrentStreams option ([c028e64](https://github.com/st0o0/TurboHTTP/commit/c028e6416d2b6e8202a9e2d4fbc9a540abf650e3))
* **h3:** add MaxConcurrentStreams option to Http3Options ([284195d](https://github.com/st0o0/TurboHTTP/commit/284195d84e4862a0431f6bd9c0282be334281936))
* **h3:** add MaxReconnectBufferSize option ([b89024a](https://github.com/st0o0/TurboHTTP/commit/b89024ae4313ac71e30c71c91bbd36c6e7f68316))
* **h3:** enforce SETTINGS_MAX_FIELD_SECTION_SIZE on encode and decode ([79e04bb](https://github.com/st0o0/TurboHTTP/commit/79e04bb54d69d445c0a880d4622a30e6095b6517))
* **h3:** pass MaxConcurrentStreams from options to StreamTracker ([1cbb663](https://github.com/st0o0/TurboHTTP/commit/1cbb663b57c0af1e46541dd34cb4f1fb6baa1ae9))
* **h3:** populate SETTINGS frame with configured parameters ([8b1a657](https://github.com/st0o0/TurboHTTP/commit/8b1a6571b6b759b307cfbd25ac86f1028ef1c393))
* **h3:** reject duplicate critical unidirectional streams ([a97f815](https://github.com/st0o0/TurboHTTP/commit/a97f815af3fd0fbe986dcf03184835351f6678f9))
* **h3:** validate Content-Length against accumulated body length ([d93851e](https://github.com/st0o0/TurboHTTP/commit/d93851ec0d18af8459499be97db885f4131a64a7))
* **h3:** wire MaxConcurrentStreams into ProtocolCoreBuilder slot concurrency ([2832dff](https://github.com/st0o0/TurboHTTP/commit/2832dffd0556038b8f7e20be6a2d30c10c1af7e7))


### Bug Fixes

* checkout with lfs ([7a470ab](https://github.com/st0o0/TurboHTTP/commit/7a470ab53e058868e2f2e39e520f9e7649470f23))
* **h3:** improve control stream stability ([153a37b](https://github.com/st0o0/TurboHTTP/commit/153a37b108da7b47ac50189427aa3daef1648fb8))
* **h3:** wire MaxConnectionsPerServer and MaxConcurrentStreams to QUIC transport ([2c6b6be](https://github.com/st0o0/TurboHTTP/commit/2c6b6be8fd5bc3eaf30b3fb5bedad8295fb11383))
* **lfs:** migrate logo files to LFS pointers without history rewrite ([6c0e24a](https://github.com/st0o0/TurboHTTP/commit/6c0e24af914588a8a88155dc723a0fbc0359d0a0))
* **lint-config:** Disable case rules ([9f4ed18](https://github.com/st0o0/TurboHTTP/commit/9f4ed18f87f31b8235fea986b4984c3f058e0ee1))
* public api changes ([2d6dfa4](https://github.com/st0o0/TurboHTTP/commit/2d6dfa4f114bfa3ada759bee4823dbd23c887c53))
* **quic:** check RemoteEndPoint for connection migration instead of LocalEndPoint ([8692df6](https://github.com/st0o0/TurboHTTP/commit/8692df6a22836678e504a28a8aa11228881af3c9))


### Performance

* **bench:** tune HTTP/3 benchmark settings for higher throughput ([cc7ae18](https://github.com/st0o0/TurboHTTP/commit/cc7ae18a6f41198ea9ecb1dc86f68483e21d5e96))
* **h2:** increase header table size to 64KB and enable Huffman encoding ([a5478ce](https://github.com/st0o0/TurboHTTP/commit/a5478ce81630f9d5cba5dbd5af378a2b9403bd03))
* **h3:** replace ToArray with ArrayPool in FlushOutbound ([85bb516](https://github.com/st0o0/TurboHTTP/commit/85bb51685776ef059b87a0204a49765cad9c6b2e))
* **h3:** replace ToArray with ArrayPool in FlushResponses ([83d2ce5](https://github.com/st0o0/TurboHTTP/commit/83d2ce5b26da76db0aff8ee1b346e5658f3d796e))
* **hpack:** replace List with ring buffer to eliminate O(n) eviction ([f2133fa](https://github.com/st0o0/TurboHTTP/commit/f2133fab759bcff05e13034c1cd2c712eea75247))
* **quic:** Conflate outbound messages ([c12d2b0](https://github.com/st0o0/TurboHTTP/commit/c12d2b051cc8f0b7cddba2d2031922a6580c3e12))
* **quic:** move connection migration check from hot path to 5s timer ([fa6c899](https://github.com/st0o0/TurboHTTP/commit/fa6c8998d226ba2c566587b3ee0de68c12af3fce))


### Documentation

* update ([968cbfe](https://github.com/st0o0/TurboHTTP/commit/968cbfefd731af511f4451202a7e7fbed2f4f59b))
* update LikeC4 ([478dbb4](https://github.com/st0o0/TurboHTTP/commit/478dbb463885e4e00abf86a3dfe7bffbeb058b5e))


### Dependencies

* bump googleapis/release-please-action from 4 to 5 ([71f43ce](https://github.com/st0o0/TurboHTTP/commit/71f43cec04f437d8f37a1b384e7d4f152ea0b293))

## [0.2.0](https://github.com/st0o0/TurboHTTP/compare/v0.1.3...v0.2.0) (2026-05-04)


### Features

* add QUIC transport implementation ([780d0c0](https://github.com/st0o0/TurboHTTP/commit/780d0c0c6e3c685c95b4be29105053c3e366cbd7))
* add Servus.Akka.TestKit dependency ([98bffd1](https://github.com/st0o0/TurboHTTP/commit/98bffd190ad144fb30682ac5bfad143724978db6))
* add Servus.Akka.Transport listener ([70ad8bf](https://github.com/st0o0/TurboHTTP/commit/70ad8bf99cfffde2f09148cfc3c8bf59df6aa613))
* add ServusTrace integration ([722ea70](https://github.com/st0o0/TurboHTTP/commit/722ea7047977854b8b9cd361e0f22bc580ea8b87))
* add TCP transport implementation ([ebf6689](https://github.com/st0o0/TurboHTTP/commit/ebf66890bb54ab84565b32456b516f64b449606e))


### Bug Fixes

* **ci:** adjust release manifest directory ([44980a5](https://github.com/st0o0/TurboHTTP/commit/44980a5bc10fec9ab44656b037e3ed621d14e6dd))
* **ci:** integrate deps commit type ([5f88452](https://github.com/st0o0/TurboHTTP/commit/5f884529c0df9fa64889ea2ae38c42b0fd27a631))
* **commitlint:** ignore dependabot commits ([f3a662a](https://github.com/st0o0/TurboHTTP/commit/f3a662aced507f71057ba1d88416c018cbe42e88))
* minor fixes ([2d62179](https://github.com/st0o0/TurboHTTP/commit/2d62179e9c8a91c4591f50d33bfb3b39ff8921bc))
* minor fixes ([f1cb795](https://github.com/st0o0/TurboHTTP/commit/f1cb79575a48d17e6ac6f3392640142662c3b6bf))
* minor transport fix ([9b1bba2](https://github.com/st0o0/TurboHTTP/commit/9b1bba223aeffc63b87dd63db3714c4d54a53e80))
* **readme:** Correct workflow badges ([1392385](https://github.com/st0o0/TurboHTTP/commit/13923855b98ce487754d43baac42b13bdd720c32))
* **release:** correct config paths ([a6ad77e](https://github.com/st0o0/TurboHTTP/commit/a6ad77ee7bae28faa1515ac9cd7562ece4731cff))


### Performance

* enhance HTTP/2 and HTTP/3 transport performance and streaming ([74d0b30](https://github.com/st0o0/TurboHTTP/commit/74d0b30d7105482d1933354c09ea6aedd9cfd5f3))


### Documentation

* update Obsidian notes ([b933151](https://github.com/st0o0/TurboHTTP/commit/b93315141e1724b67e280f1f5f005584715ad55b))


### Dependencies

* Bump actions/checkout from 4 to 6 ([a9a12fa](https://github.com/st0o0/TurboHTTP/commit/a9a12fa54ea75f44dfe00098bb78b0ee71348392))
* bump actions/deploy-pages from 4 to 5 ([6998305](https://github.com/st0o0/TurboHTTP/commit/69983054d11d8ce88462a352b0611a0a0e5dabd9))
* bump actions/setup-node from 4 to 6 ([299470c](https://github.com/st0o0/TurboHTTP/commit/299470c0e773bbf67f43b49b6b577a93125ecb23))
* Bump actions/upload-pages-artifact from 3 to 5 ([233b563](https://github.com/st0o0/TurboHTTP/commit/233b563c65f39d94dc063bd61edb7fccf1c9f9fe))
* bump amannn/action-semantic-pull-request from 5 to 6 ([5f3a418](https://github.com/st0o0/TurboHTTP/commit/5f3a4182afbc23598ad7c4065458533250c62d7f))
