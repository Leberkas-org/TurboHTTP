import type { Theme } from 'vitepress'
import DefaultTheme from 'vitepress/theme'
import LikeC4Diagram from '../components/LikeC4Diagram.vue'
import './custom.css'

export default {
    extends: DefaultTheme,
    enhanceApp({ app })
    {
        app.component('LikeC4Diagram', LikeC4Diagram)
    },
} satisfies Theme
