import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'

export default defineConfig({
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: 5173,
    // 使用中间件处理 API 请求
    middlewareMode: false
  },
  // 配置 Vite 中间件来处理 API 请求
  configureServer(server) {
    // 在这里可以添加自定义中间件
    server.middlewares.use(async (req, res, next) => {
      // 如果是 API 请求，转发到 Express 服务器
      if (req.url.startsWith('/api/')) {
        // 让 Express 服务器处理
        next()
      } else {
        next()
      }
    })
  }
})
