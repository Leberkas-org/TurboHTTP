# Changelog

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
