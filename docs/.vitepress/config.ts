import { defineConfig } from 'vitepress'
import { LikeC4VitePlugin } from 'likec4/vite-plugin'

export default defineConfig({
    vite: {
        plugins: [
            LikeC4VitePlugin({ workspace: './likec4' }),
        ],
    },
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
            { text: 'Getting Started', link: '/getting-started/' },
            { text: 'Scenarios', link: '/scenarios' },
            { text: 'Client', link: '/client/' },
            { text: 'Server', link: '/server/' },
            { text: 'Architecture', link: '/architecture/pipeline' },
            { text: 'API', link: '/api/' },
        ],

        sidebar: {
            '/getting-started/': [
                {
                    text: 'Getting Started',
                    items: [
                        { text: 'Overview', link: '/getting-started/' },
                        { text: 'Client Quick Start', link: '/getting-started/client' },
                        { text: 'Server Quick Start', link: '/getting-started/server' },
                        { text: 'Architecture Overview', link: '/getting-started/architecture' },
                        { text: 'Migration from HttpClient', link: '/getting-started/migration' },
                    ],
                },
            ],
            '/scenarios': [
                {
                    text: 'Scenarios',
                    items: [
                        { text: 'Overview', link: '/scenarios' },
                    ],
                },
            ],
            '/client/': [
                {
                    text: 'Client',
                    items: [
                        { text: 'Overview', link: '/client/' },
                        { text: 'Installation & Setup', link: '/client/installation' },
                        { text: 'Configuration', link: '/client/configuration' },
                        { text: 'Connection Pooling', link: '/client/connection-pooling' },
                        { text: 'Automatic Retries', link: '/client/retries' },
                        { text: 'HTTP Caching', link: '/client/caching' },
                        { text: 'Cookie Management', link: '/client/cookies' },
                        { text: 'Redirects', link: '/client/redirects' },
                        { text: 'Content Encoding', link: '/client/content-encoding' },
                        { text: 'HTTP/2 & Multiplexing', link: '/client/http2' },
                        { text: 'HTTP/3 & QUIC', link: '/client/http3' },
                        { text: 'Real-World Scenarios', link: '/client/scenarios' },
                        { text: 'Troubleshooting', link: '/client/troubleshooting' },
                    ],
                },
            ],
            '/server/': [
                {
                    text: 'Server',
                    items: [
                        { text: 'Overview', link: '/server/' },
                        { text: 'Installation & Setup', link: '/server/installation' },
                        { text: 'Configuration', link: '/server/configuration' },
                        { text: 'Hosting & Lifecycle', link: '/server/hosting' },
                        { text: 'Middleware Pipeline', link: '/server/middleware' },
                        { text: 'Routing', link: '/server/routing' },
                        { text: 'Parameter Binding', link: '/server/binding' },
                        { text: 'Validation', link: '/server/validation' },
                        { text: 'Entity Gateway', link: '/server/entity-gateway' },
                        { text: 'Real-World Scenarios', link: '/server/scenarios' },
                        { text: 'Troubleshooting', link: '/server/troubleshooting' },
                    ],
                },
            ],
            '/api/': [
                {
                    text: 'API Reference',
                    items: [
                        { text: 'Overview', link: '/api/' },
                        { text: 'Client API', link: '/api/client' },
                        { text: 'Client Options', link: '/api/client-options' },
                        { text: 'Feature Options', link: '/api/feature-options' },
                        { text: 'Server API', link: '/api/server' },
                        { text: 'Entity Gateway API', link: '/api/entity-gateway' },
                    ],
                },
            ],
            '/architecture/': [
                {
                    text: 'Client Architecture',
                    items: [
                        { text: 'Request Pipeline', link: '/architecture/pipeline' },
                        { text: 'Protocol Engines', link: '/architecture/engines' },
                    ],
                },
                {
                    text: 'Server Architecture',
                    items: [
                        { text: 'Request Pipeline', link: '/architecture/server-pipeline' },
                        { text: 'Protocol Engines', link: '/architecture/server-engines' },
                    ],
                },
            ],
        },

        socialLinks: [
            { icon: 'github', link: 'https://github.com/leberkas-org/TurboHTTP' },
        ],

        footer: {
            message: 'Released under the MIT License.',
            copyright: 'Copyright © TurboHTTP Contributors',
        },
    },
})
