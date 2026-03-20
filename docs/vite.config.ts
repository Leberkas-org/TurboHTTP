import { defineConfig } from 'vite'
import { LikeC4VitePlugin } from 'likec4/vite-plugin'

export default defineConfig({
    plugins: [
        LikeC4VitePlugin({
            workspace: './likec4',
        }),
    ],
})
