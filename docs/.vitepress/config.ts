import { defineConfig } from 'vitepress'

export default defineConfig({
    title: 'TurboHttp',
    description: 'High-performance HTTP client library for .NET built on Akka.Streams — HTTP/1.0, HTTP/1.1, and HTTP/2 with automatic retries, caching, cookies, and connection pooling.',
    base: '/TurboHttp/',
    ignoreDeadLinks: true,
    head: [
        ['link', { rel: 'icon', type: 'image/png', href: '/TurboHttp/logo/icon.png' }],
    ],
    themeConfig: {
        logo: '/logo/logo_small.png',
        search: {
            provider: 'local',
        },

        nav: [
            { text: 'Home', link: '/' },
            { text: 'Guide', link: '/guide/' },
            { text: 'Architecture', link: '/architecture/' },
            { text: 'API', link: '/api/' },
            { text: 'Why TurboHttp?', link: '/why/' },
        ],

        sidebar: {
            '/guide/': [
                {
                    text: 'Getting Started',
                    items: [
                        { text: 'Quick Start', link: '/guide/' },
                        { text: 'Installation & Setup', link: '/guide/installation' },
                        { text: 'Configuration', link: '/guide/configuration' },
                        { text: 'Migration from HttpClient', link: '/guide/migration' },
                    ],
                },
                {
                    text: 'Features',
                    items: [
                        { text: 'Automatic Retries', link: '/guide/retries' },
                        { text: 'HTTP Caching', link: '/guide/caching' },
                        { text: 'Cookie Management', link: '/guide/cookies' },
                        { text: 'Redirects', link: '/guide/redirects' },
                        { text: 'Content Encoding', link: '/guide/content-encoding' },
                        { text: 'Connection Pooling', link: '/guide/connection-pooling' },
                        { text: 'HTTP/2 & Multiplexing', link: '/guide/http2' },
                    ],
                },
                {
                    text: 'Help',
                    items: [
                        { text: 'Troubleshooting & FAQ', link: '/guide/troubleshooting' },
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
                    text: 'Why TurboHttp?',
                    items: [
                        { text: 'Comparison', link: '/why/' },
                    ],
                },
            ],
            '/performance/': [
                {
                    text: 'Performance',
                    items: [
                        { text: 'Release 1.0 Benchmarks', link: '/performance/release-1.0' },
                    ],
                },
            ],
        },

        socialLinks: [
            { icon: 'github', link: 'https://github.com/st0o0/TurboHttp' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © TurboHttp Contributors',
        },
    },
})
