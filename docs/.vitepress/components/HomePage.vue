<script setup lang="ts">
const features = [
    {
        title: 'Multi-Protocol',
        description: 'HTTP/1.0, 1.1, 2 & 3 (QUIC) — automatic version negotiation, HPACK/QPACK compression, multiplexed streams.',
    },
    {
        title: 'Zero Allocation',
        description: 'Span<T>, Memory<byte>, and pooled buffers throughout. Zero GC pressure on the hot path.',
    },
    {
        title: 'Smart Retry & Cache',
        description: 'Idempotency-aware retries + in-memory LRU cache with ETag support. Built in, not bolted on.',
    },
    {
        title: 'Middleware & Routing',
        description: 'ASP.NET Core-style pipeline with Use/Run/Map. Entity gateway routes requests to Akka.NET actors.',
    },
    {
        title: 'Connection Pooling',
        description: 'Per-host pools with idle eviction, automatic reconnect, and configurable concurrency limits.',
    },
    {
        title: 'Standalone Server',
        description: 'Actor-based HTTP server with TCP/QUIC transport, supervisor hierarchy, and graceful shutdown.',
    },
]

const comparison = [
    { feature: 'HTTP/2 Multiplexing', httpClient: 'Partial', refit: 'Partial', flurl: 'No', turbo: 'Full' },
    { feature: 'HTTP/3 (QUIC)', httpClient: 'Partial', refit: 'No', flurl: 'No', turbo: 'Full' },
    { feature: 'Automatic Retries', httpClient: 'Polly needed', refit: 'Polly needed', flurl: 'No', turbo: 'Built-in' },
    { feature: 'HTTP Caching', httpClient: 'No', refit: 'No', flurl: 'No', turbo: 'Built-in' },
    { feature: 'Cookie Management', httpClient: 'Manual', refit: 'Manual', flurl: 'Manual', turbo: 'Automatic' },
    { feature: 'Backpressure', httpClient: 'No', refit: 'No', flurl: 'No', turbo: 'Akka.Streams' },
    { feature: 'Zero-alloc Internals', httpClient: 'Partial', refit: 'No', flurl: 'No', turbo: 'Full' },
    { feature: 'Channel-based API', httpClient: 'No', refit: 'No', flurl: 'No', turbo: 'Yes' },
]

const clientCode = `builder.Services.AddTurboHttpClient("api", options =>
{
    options.BaseAddress = new Uri("https://api.example.com");
})
.WithRetry()
.WithCache()
.WithCookies()
.WithRedirect();

var client = factory.CreateClient("api");
var response = await client.SendAsync(request, ct);`

const serverCode = `builder.Services.AddTurboKestrel(options =>
{
    options.ListenLocalhost(5100);
});

app.MapTurboGet("/health", () => new { status = "ok" });
app.MapTurboGet("/users/{id}", (int id) =>
    new { id, name = "User " + id });

await app.RunAsync();`

import { ref } from 'vue'
const activeTab = ref<'client' | 'server'>('client')
</script>

<template>
    <div class="home-page">
        <!-- Hero -->
        <section class="hero">
            <div class="hero-content">
                <div class="hero-text">
                    <h1 class="hero-title"><span class="turbo">Turbo</span><span class="http">HTTP</span></h1>
                    <p class="hero-tagline">High-Performance HTTP Client & Server for .NET</p>
                    <div class="hero-badges">
                        <span class="badge">Zero Alloc</span>
                        <span class="badge violet">HTTP/1–3 + QUIC</span>
                        <span class="badge">Backpressure</span>
                    </div>
                    <div class="hero-actions">
                        <a href="/getting-started/" class="action-btn primary">Get Started</a>
                        <a href="https://github.com/leberkas-org/TurboHTTP" class="action-btn secondary" target="_blank">GitHub</a>
                    </div>
                </div>
                <div class="hero-code">
                    <div class="code-header">
                        <button
                            :class="['tab', { active: activeTab === 'client' }]"
                            @click="activeTab = 'client'"
                        >Client</button>
                        <button
                            :class="['tab', 'tab-server', { active: activeTab === 'server' }]"
                            @click="activeTab = 'server'"
                        >Server</button>
                    </div>
                    <div class="code-block-stack">
                        <pre class="code-block" :class="{ inactive: activeTab !== 'client' }"><code>{{ clientCode }}</code></pre>
                        <pre class="code-block" :class="{ inactive: activeTab !== 'server' }"><code>{{ serverCode }}</code></pre>
                    </div>
                </div>
            </div>
        </section>

        <!-- Features -->
        <section class="features">
            <h2 class="section-title">Features</h2>
            <div class="feature-grid">
                <div v-for="f in features" :key="f.title" class="feature-card">
                    <h3>{{ f.title }}</h3>
                    <p>{{ f.description }}</p>
                </div>
            </div>
        </section>

        <!-- Comparison -->
        <section class="comparison">
            <h2 class="section-title">vs. the Alternatives</h2>
            <div class="table-wrapper">
                <table>
                    <thead>
                        <tr>
                            <th>Feature</th>
                            <th>HttpClient</th>
                            <th>Refit</th>
                            <th>Flurl</th>
                            <th class="highlight">TurboHTTP</th>
                        </tr>
                    </thead>
                    <tbody>
                        <tr v-for="row in comparison" :key="row.feature">
                            <td>{{ row.feature }}</td>
                            <td>{{ row.httpClient }}</td>
                            <td>{{ row.refit }}</td>
                            <td>{{ row.flurl }}</td>
                            <td class="highlight">{{ row.turbo }}</td>
                        </tr>
                    </tbody>
                </table>
            </div>
        </section>

        <!-- Install -->
        <section class="install">
            <h2 class="section-title">Get Started</h2>
            <div class="install-code">
                <pre><code>dotnet add package TurboHTTP</code></pre>
            </div>
            <div class="install-links">
                <a href="/getting-started/" class="install-link">Getting Started</a>
                <a href="/client/" class="install-link">Client Docs</a>
                <a href="/server/" class="install-link">Server Docs</a>
            </div>
        </section>
    </div>
</template>

<style scoped>
.home-page {
    max-width: 1152px;
    margin: 0 auto;
    padding: 0 24px;
}

/* Hero */
.hero {
    padding: 64px 0 48px;
}

.hero-content {
    display: flex;
    gap: 48px;
    align-items: center;
}

.hero-text {
    flex: 1;
}

.hero-title {
    font-size: 48px;
    font-weight: 700;
    margin: 0 0 8px;
    line-height: 1.1;
}

.hero-title .turbo {
    color: var(--vp-c-brand-1);
}

.hero-title .http {
    color: #8b5cf6;
}

.dark .hero-title .http {
    color: #a78bfa;
}

.hero-tagline {
    font-size: 20px;
    color: var(--vp-c-text-2);
    margin: 0 0 24px;
    line-height: 1.4;
}

.hero-badges {
    display: flex;
    gap: 8px;
    margin-bottom: 24px;
    flex-wrap: wrap;
}

.badge {
    padding: 4px 12px;
    border-radius: 12px;
    font-size: 13px;
    font-weight: 600;
    background: var(--vp-c-brand-soft);
    color: var(--vp-c-brand-1);
}

.badge.violet {
    background: rgba(139, 92, 246, 0.14);
    color: #8b5cf6;
}

.dark .badge.violet {
    background: rgba(167, 139, 250, 0.14);
    color: #a78bfa;
}

.hero-actions {
    display: flex;
    gap: 12px;
}

.action-btn {
    padding: 10px 24px;
    border-radius: 8px;
    font-size: 15px;
    font-weight: 600;
    text-decoration: none;
    transition: opacity 0.2s;
}

.action-btn:hover {
    opacity: 0.9;
}

.action-btn.primary {
    background: var(--vp-c-brand-1);
    color: white;
}

.action-btn.secondary {
    border: 1px solid var(--vp-c-divider);
    color: var(--vp-c-text-1);
}

/* Hero code block */
.hero-code {
    flex: 1;
    border-radius: 12px;
    overflow: hidden;
    border: 1px solid var(--vp-c-divider);
}

.code-header {
    display: flex;
    background: var(--vp-c-bg-soft);
    border-bottom: 1px solid var(--vp-c-divider);
}

.tab {
    padding: 8px 16px;
    border: none;
    background: transparent;
    color: var(--vp-c-text-2);
    cursor: pointer;
    font-size: 13px;
    font-weight: 500;
    border-bottom: 2px solid transparent;
}

.tab.active {
    color: var(--vp-c-brand-1);
    border-bottom-color: var(--vp-c-brand-1);
}

.tab-server.active {
    color: #8b5cf6;
    border-bottom-color: #8b5cf6;
}

.dark .tab-server.active {
    color: #a78bfa;
    border-bottom-color: #a78bfa;
}

.code-block-stack {
    display: grid;
    background: var(--vp-code-block-bg);
}

.code-block-stack .code-block {
    grid-area: 1 / 1;
}

.code-block.inactive {
    visibility: hidden;
}

.code-block {
    margin: 0;
    padding: 20px 24px;
    background: var(--vp-code-block-bg);
    overflow-x: auto;
}

.code-block code {
    font-family: var(--vp-font-family-mono);
    font-size: 13px;
    line-height: 1.6;
    color: var(--vp-c-text-1);
}

/* Features */
.features {
    padding: 48px 0;
}

.section-title {
    font-size: 28px;
    font-weight: 700;
    text-align: center;
    margin: 0 0 32px;
    color: var(--vp-c-text-1);
}

.feature-grid {
    display: grid;
    grid-template-columns: repeat(3, 1fr);
    gap: 16px;
}

.feature-card {
    padding: 24px;
    border-radius: 12px;
    border: 1px solid var(--vp-c-divider);
    background: var(--vp-c-bg-soft);
    transition: border-color 0.2s;
}

.feature-card:hover {
    border-color: var(--vp-c-brand-1);
}

.feature-card:nth-child(even):hover {
    border-color: #8b5cf6;
}

.dark .feature-card:nth-child(even):hover {
    border-color: #a78bfa;
}

.feature-card h3 {
    margin: 0 0 8px;
    font-size: 16px;
    color: var(--vp-c-text-1);
}

.feature-card p {
    margin: 0;
    font-size: 14px;
    color: var(--vp-c-text-2);
    line-height: 1.5;
}

/* Comparison */
.comparison {
    padding: 48px 0;
}

.table-wrapper {
    overflow-x: auto;
    border-radius: 12px;
    border: 1px solid var(--vp-c-divider);
}

.comparison table {
    width: 100%;
    border-collapse: collapse;
    font-size: 14px;
}

.comparison th,
.comparison td {
    padding: 12px 16px;
    text-align: left;
    border-bottom: 1px solid var(--vp-c-divider);
}

.comparison th {
    font-weight: 600;
    color: var(--vp-c-text-1);
    background: var(--vp-c-bg-soft);
}

.comparison th.highlight {
    background: var(--vp-c-bg-soft);
    color: var(--vp-c-text-1);
}

.comparison td {
    color: var(--vp-c-text-1);
}

.comparison tbody tr:nth-child(odd) {
    background: var(--vp-c-brand-soft);
}

.comparison tbody tr:nth-child(even) {
    background: rgba(139, 92, 246, 0.08);
}

.dark .comparison tbody tr:nth-child(even) {
    background: rgba(167, 139, 250, 0.08);
}

.comparison .highlight {
    font-weight: 600;
    color: var(--vp-c-text-1);
}

.comparison tbody tr {
    transition: background 0.2s;
}

.comparison tbody tr:nth-child(odd):hover {
    background: rgba(16, 185, 129, 0.22);
}

.dark .comparison tbody tr:nth-child(odd):hover {
    background: rgba(52, 211, 153, 0.22);
}

.comparison tbody tr:nth-child(even):hover {
    background: rgba(139, 92, 246, 0.16);
}

.dark .comparison tbody tr:nth-child(even):hover {
    background: rgba(167, 139, 250, 0.16);
}

/* Install */
.install {
    padding: 48px 0 64px;
    text-align: center;
}

.install-code {
    max-width: 400px;
    margin: 0 auto 24px;
}

.install-code pre {
    padding: 16px 24px;
    border-radius: 8px;
    background: var(--vp-code-block-bg);
    border: 1px solid var(--vp-c-divider);
}

.install-code code {
    font-family: var(--vp-font-family-mono);
    font-size: 15px;
    color: var(--vp-c-text-1);
}

.install-links {
    display: flex;
    justify-content: center;
    gap: 16px;
}

.install-link {
    padding: 8px 20px;
    border-radius: 8px;
    border: 1px solid var(--vp-c-divider);
    color: var(--vp-c-text-1);
    text-decoration: none;
    font-size: 14px;
    font-weight: 500;
    transition: border-color 0.2s, color 0.2s;
}

.install-link:nth-child(odd):hover {
    border-color: var(--vp-c-brand-1);
    color: var(--vp-c-brand-1);
}

.install-link:nth-child(even):hover {
    border-color: #8b5cf6;
    color: #8b5cf6;
}

.dark .install-link:nth-child(even):hover {
    border-color: #a78bfa;
    color: #a78bfa;
}

/* Responsive */
@media (max-width: 768px) {
    .hero-content {
        flex-direction: column;
    }

    .hero-title {
        font-size: 36px;
    }

    .feature-grid {
        grid-template-columns: 1fr;
    }

    .install-links {
        flex-direction: column;
        align-items: center;
    }
}
</style>
