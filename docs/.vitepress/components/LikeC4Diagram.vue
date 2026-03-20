<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { useData } from 'vitepress'

const props = defineProps<{
    viewId: string
    height?: number
    interactive?: boolean
    showNavigation?: boolean
}>()

const { isDark } = useData()
const colorScheme = computed(() => isDark.value ? 'dark' : 'light')

const containerRef = ref<HTMLElement | null>(null)
const status = ref<'loading' | 'ready' | 'error'>('loading')

let unmountRoot: (() => void) | null = null

async function renderDiagram()
{
    if (typeof window === 'undefined') return
    const el = containerRef.value
    if (!el) return

    unmountRoot?.()
    unmountRoot = null
    status.value = 'loading'

    try
    {
        const [{ createElement }, { createRoot }, { LikeC4View }] = await Promise.all([
            import('react'),
            import('react-dom/client'),
            import('likec4:react'),
        ])

        const root = createRoot(el)
        root.render(createElement(LikeC4View, {
            viewId: props.viewId,
            colorScheme: colorScheme.value,
            fitView: true,
            pannable: props.interactive !== false,
            zoomable: props.interactive !== false,
            background: 'transparent',
            keepAspectRatio: true,
        }))
        status.value = 'ready'
        unmountRoot = () => root.unmount()
    }
    catch (err)
    {
        console.error('[LikeC4Diagram] Failed to render diagram:', err)
        status.value = 'error'
    }
}

onMounted(renderDiagram)

watch(() => props.viewId, renderDiagram)
watch(colorScheme, renderDiagram)

onUnmounted(() =>
{
    unmountRoot?.()
    unmountRoot = null
})
</script>

<template>
    <div class="likec4-diagram" :style="height ? { height: `${height}px` } : {}">
        <!-- React mount target — always in the DOM so React can size itself correctly -->
        <div ref="containerRef" class="likec4-container" />

        <!-- Loading overlay — shown until React has rendered -->
        <div v-if="status === 'loading'" class="likec4-overlay">
            <span class="likec4-loading-text">Loading diagram…</span>
        </div>

        <!-- Error fallback — shown when the interactive component fails to load -->
        <div v-else-if="status === 'error'" class="likec4-overlay likec4-fallback">
            <p>Interactive diagram could not be loaded.</p>
            <img
                :src="`/TurboHttp/diagrams/${viewId}.svg`"
                :alt="`Architecture diagram: ${viewId}`"
                class="likec4-fallback-img"
            />
        </div>
    </div>
</template>

<style scoped>
.likec4-diagram {
    width: 100%;
    min-height: 400px;
    aspect-ratio: 16 / 10;
    border-radius: 8px;
    border: 1px solid var(--vp-c-divider);
    overflow: hidden;
    margin: 1.5rem 0;
    position: relative;
    background: transparent;
}

.likec4-container {
    width: 100%;
    height: 100%;
}

.likec4-overlay {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 0.75rem;
    padding: 1rem;
    background: var(--vp-c-bg-soft);
}

.likec4-loading-text {
    color: var(--vp-c-text-2);
    font-size: 0.9em;
}

.likec4-fallback {
    color: var(--vp-c-text-2);
    font-size: 0.9em;
    text-align: center;
}

.likec4-fallback-img {
    max-width: 100%;
    max-height: 80%;
    object-fit: contain;
}
</style>
