import { defineConfig } from 'vitepress'

export default defineConfig({
    title: 'TurboHTTP',
    description: 'High-performance HTTP client and server for .NET built on Akka.Streams — HTTP/1.0, HTTP/1.1, HTTP/2, and HTTP/3 (QUIC) with automatic retries, caching, cookies, connection pooling, middleware pipeline, routing, and entity gateway.',
    base: '/',
    head: [
        ['link', { rel: 'icon', type: 'image/png', href: '/logo/icon.png' }],
    ],
    themeConfig: {
        logo: '/logo/icon.png',
        search: {
            provider: 'local',
        },

        nav: [
            { text: 'Home', link: '/' },
            { text: 'Quick Guide', link: '/quickstart/' },
            { text: 'Client', link: '/client/' },
            { text: 'Server', link: '/server/' },
            { text: 'Architecture', link: '/architecture/' },
            { text: 'API', link: '/api/' },
            { text: 'Why TurboHTTP?', link: '/why/' },
        ],

        sidebar: {
            '/quickstart/': [
                {
                    text: 'Quick Guide',
                    items: [
                        { text: 'Quick Guide', link: '/quickstart/' },
                    ],
                },
            ],
            '/client/': [
                {
                    text: 'Getting Started',
                    items: [
                        { text: 'Quick Start', link: '/client/' },
                        { text: 'Installation & Setup', link: '/client/installation' },
                        { text: 'Configuration', link: '/client/configuration' },
                        { text: 'Migration from HttpClient', link: '/client/migration' },
                    ],
                },
                {
                    text: 'Features',
                    items: [
                        { text: 'Automatic Retries', link: '/client/retries' },
                        { text: 'HTTP Caching', link: '/client/caching' },
                        { text: 'Cookie Management', link: '/client/cookies' },
                        { text: 'Redirects', link: '/client/redirects' },
                        { text: 'Content Encoding', link: '/client/content-encoding' },
                        { text: 'Connection Pooling', link: '/client/connection-pooling' },
                        { text: 'HTTP/2 & Multiplexing', link: '/client/http2' },
                        { text: 'HTTP/3 & QUIC', link: '/client/http3' },
                    ],
                },
                {
                    text: 'Help',
                    items: [
                        { text: 'Troubleshooting & FAQ', link: '/client/troubleshooting' },
                    ],
                },
            ],
            '/server/': [
                {
                    text: 'Getting Started',
                    items: [
                        { text: 'Quick Start', link: '/server/' },
                        { text: 'Installation & Setup', link: '/server/installation' },
                        { text: 'Configuration', link: '/server/configuration' },
                        { text: 'Hosting & Lifecycle', link: '/server/hosting' },
                    ],
                },
                {
                    text: 'Features',
                    items: [
                        { text: 'Middleware Pipeline', link: '/server/middleware' },
                        { text: 'Routing', link: '/server/routing' },
                        { text: 'Entity Gateway', link: '/server/entity-gateway' },
                    ],
                },
                {
                    text: 'Advanced',
                    items: [
                        { text: 'Parameter Binding', link: '/server/binding' },
                        { text: 'Validation', link: '/server/validation' },
                    ],
                },
                {
                    text: 'Help',
                    items: [
                        { text: 'Troubleshooting', link: '/server/troubleshooting' },
                    ],
                },
            ],
            '/architecture/': [
                {
                    text: 'Architecture',
                    items: [
                        { text: 'Overview', link: '/architecture/' },
                        { text: 'Handler Design', link: '/architecture/handlers' },
                        { text: 'Architectural Layers', link: '/architecture/layers' },
                        { text: 'Protocol Engines', link: '/architecture/engines' },
                        { text: 'Request Pipeline', link: '/architecture/pipeline' },
                        { text: 'End-to-End Scenarios', link: '/architecture/scenarios' },
                        { text: 'Extending the Pipeline', link: '/architecture/extending' },
                    ],
                },
            ],
            '/api/': [
                {
                    text: 'API Reference',
                    items: [
                        { text: 'Overview', link: '/api/' },
                    ],
                },
            ],
            '/why/': [
                {
                    text: 'Why TurboHTTP?',
                    items: [
                        { text: 'Comparison', link: '/why/' },
                    ],
                },
            ],
        },

        socialLinks: [
            { icon: 'github', link: 'https://github.com/st0o0/TurboHTTP' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © TurboHTTP Contributors',
        },
    },
})
