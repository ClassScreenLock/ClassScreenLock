import { createApp } from 'vue'
import './style.css'
import App from './App.vue'
import router from './router'

// 初始化字体大小
const initFontSize = () => {
  const fontSizeConfig = {
    small: { base: 14, small: 12, medium: 13, large: 16 },
    medium: { base: 16, small: 13, medium: 15, large: 18 },
    large: { base: 18, small: 14, medium: 16, large: 20 },
    xl: { base: 20, small: 16, medium: 18, large: 22 }
  }
  
  const savedFontSize = localStorage.getItem('fontSize') || 'medium'
  const config = fontSizeConfig[savedFontSize]
  const root = document.documentElement
  
  root.style.setProperty('--fui-font-size-base', `${config.base}px`)
  root.style.setProperty('--fui-font-size-small', `${config.small}px`)
  root.style.setProperty('--fui-font-size-medium', `${config.medium}px`)
  root.style.setProperty('--fui-font-size-large', `${config.large}px`)
}

initFontSize()

const app = createApp(App)
app.use(router)
app.mount('#app')
