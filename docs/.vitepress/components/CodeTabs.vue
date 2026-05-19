<script setup lang="ts">
import { ref } from 'vue'

interface Tab {
    label: string
    language?: string
    code: string
}

const props = defineProps<{
    tabs: Tab[]
}>()

const activeIndex = ref(0)
</script>

<template>
    <div class="code-tabs">
        <div class="code-tabs-header">
            <button
                v-for="(tab, index) in tabs"
                :key="tab.label"
                :class="['code-tab-btn', { active: activeIndex === index }]"
                @click="activeIndex = index"
            >
                {{ tab.label }}
            </button>
        </div>
        <div class="code-tabs-body">
            <div
                v-for="(tab, index) in tabs"
                :key="tab.label"
                v-show="activeIndex === index"
                class="code-tab-panel"
            >
                <pre><code>{{ tab.code }}</code></pre>
            </div>
        </div>
    </div>
</template>

<style scoped>
.code-tabs {
    border: 1px solid var(--vp-c-divider);
    border-radius: 8px;
    overflow: hidden;
    margin: 16px 0;
}

.code-tabs-header {
    display: flex;
    background: var(--vp-c-bg-soft);
    border-bottom: 1px solid var(--vp-c-divider);
}

.code-tab-btn {
    padding: 8px 16px;
    border: none;
    background: transparent;
    color: var(--vp-c-text-2);
    cursor: pointer;
    font-size: 14px;
    font-weight: 500;
    transition: color 0.2s, border-color 0.2s;
    border-bottom: 2px solid transparent;
}

.code-tab-btn:hover {
    color: var(--vp-c-text-1);
}

.code-tab-btn.active {
    color: var(--vp-c-brand-1);
    border-bottom-color: var(--vp-c-brand-1);
}

.code-tabs-body {
    background: var(--vp-code-block-bg);
}

.code-tab-panel pre {
    margin: 0;
    padding: 16px 24px;
    overflow-x: auto;
}

.code-tab-panel code {
    font-family: var(--vp-font-family-mono);
    font-size: 14px;
    line-height: 1.6;
    color: var(--vp-c-text-1);
}
</style>
