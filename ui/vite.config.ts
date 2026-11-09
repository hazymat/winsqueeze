import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'
// Build straight into the shell's wwwroot, served by WebView2 from app resources
// locales/ sits at the repo root, shared with the shell
export default defineConfig({ plugins: [svelte()], base: './', server: { fs: { allow: ['..'] } }, build: { outDir: '../app/wwwroot', emptyOutDir: true } })
