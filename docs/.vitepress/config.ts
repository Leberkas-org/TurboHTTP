import { defineConfig } from 'vitepress'

export default defineConfig({
    title: 'TurboHttp',
    description: 'High-performance HTTP client library for .NET built on Akka.Streams — HTTP/1.0, HTTP/1.1, and HTTP/2 with full RFC compliance.',
    base: '/TurboHttp/',
    ignoreDeadLinks: true,
    themeConfig: {
        logo: '/logo/logo_small.svg',

        nav: [
            { text: 'Home', link: '/' },
            { text: 'Guide', link: '/guide/' },
            { text: 'Architecture', link: '/architecture/' },
            { text: 'API', link: '/api/' },
            { text: 'RFC Coverage', link: '/rfc/' },
        ],

        sidebar: {
            '/guide/': [
                {
                    text: 'Guide',
                    items: [
                        { text: 'Getting Started', link: '/guide/' },
                        { text: 'Architecture Overview', link: '/guide/architecture' },
                        { text: 'Protocol Support', link: '/guide/protocols' },
                    ],
                },
            ],
            '/architecture/': [
                {
                    text: 'Architecture',
                    items: [
                        { text: 'Overview', link: '/architecture/' },
                        { text: 'Layers', link: '/architecture/layers' },
                        { text: 'Engines', link: '/architecture/engines' },
                        { text: 'Pipeline Flow', link: '/architecture/pipeline' },
                        { text: 'Scenarios', link: '/architecture/scenarios' },
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
            '/rfc/': [
                {
                    text: 'RFC Coverage',
                    items: [
                        { text: 'Overview', link: '/rfc/' },
                        { text: 'RFC 1945 — HTTP/1.0', link: '/rfc/rfc1945' },
                        { text: 'RFC 9112 — HTTP/1.1', link: '/rfc/rfc9112' },
                        { text: 'RFC 9113 — HTTP/2', link: '/rfc/rfc9113' },
                        { text: 'RFC 7541 — HPACK', link: '/rfc/rfc7541' },
                        { text: 'RFC 9110 — HTTP Semantics', link: '/rfc/rfc9110' },
                        { text: 'RFC 9111 — Caching', link: '/rfc/rfc9111' },
                        { text: 'RFC 6265 — Cookies', link: '/rfc/rfc6265' },
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
