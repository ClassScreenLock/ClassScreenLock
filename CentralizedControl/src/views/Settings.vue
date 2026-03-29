<template>
  <div class="settings">
    <div class="settings-container">
      <div class="settings-section">
        <h2 class="section-title">
          <FluentIcons name="settings" :size="24" />
          <span>外观设置</span>
        </h2>
        <div class="settings-card">
          <div class="setting-item">
            <div class="setting-info">
              <h3>主题模式</h3>
              <p>选择您喜欢的界面主题</p>
            </div>
            <div class="theme-toggle">
              <button 
                :class="{ active: theme === 'light' }" 
                @click="setTheme('light')"
                class="theme-btn"
              >
                <FluentIcons name="sun" :size="20" />
                <span>亮色</span>
              </button>
              <button 
                :class="{ active: theme === 'dark' }" 
                @click="setTheme('dark')"
                class="theme-btn"
              >
                <FluentIcons name="moon" :size="20" />
                <span>暗黑</span>
              </button>
            </div>
          </div>
        </div>
        <div class="settings-card">
          <div class="setting-item">
            <div class="setting-info">
              <h3>字体大小</h3>
              <p>调整界面字体大小（小/标准/大/超大）</p>
            </div>
            <div class="font-size-control">
              <button 
                :class="{ active: fontSize === 'small' }" 
                @click="setFontSize('small')"
                class="font-size-btn"
              >
                <span style="font-size: 12px;">小</span>
              </button>
              <button 
                :class="{ active: fontSize === 'medium' }" 
                @click="setFontSize('medium')"
                class="font-size-btn"
              >
                <span style="font-size: 14px;">标准</span>
              </button>
              <button 
                :class="{ active: fontSize === 'large' }" 
                @click="setFontSize('large')"
                class="font-size-btn"
              >
                <span style="font-size: 16px;">大</span>
              </button>
              <button 
                :class="{ active: fontSize === 'xl' }" 
                @click="setFontSize('xl')"
                class="font-size-btn"
              >
                <span style="font-size: 18px;">超大</span>
              </button>
            </div>
          </div>
        </div>
      </div>

      <div class="settings-section">
        <h2 class="section-title">
          <FluentIcons name="globe" :size="24" />
          <span>系统设置</span>
        </h2>
        <div class="settings-card">
          <div class="setting-item">
            <div class="setting-info">
              <h3>系统名称</h3>
              <p>自定义系统显示名称</p>
            </div>
            <div class="setting-control">
              <input 
                v-model="settings.systemName" 
                type="text" 
                placeholder="ClassScreenLock 集控管理平台"
                class="input-field"
              />
            </div>
          </div>
        </div>
      </div>

      <div class="settings-section">
        <h2 class="section-title">
          <FluentIcons name="info" :size="24" />
          <span>系统信息</span>
        </h2>
        <div class="settings-card">
          <div class="info-item">
            <span class="info-label">系统版本</span>
            <span class="info-value">1.0.0</span>
          </div>
          <div class="info-item">
            <span class="info-label">运行时间</span>
            <span class="info-value">{{ uptime }}</span>
          </div>
          <div class="info-item">
            <span class="info-label">数据库状态</span>
            <span class="info-value status-online">正常</span>
          </div>
        </div>
      </div>

      <div class="settings-actions">
        <button @click="saveSettings" class="btn-primary">
          <FluentIcons name="save" :size="18" />
          <span>保存设置</span>
        </button>
        <button @click="resetSettings" class="btn-secondary">
          <FluentIcons name="refresh" :size="18" />
          <span>重置默认</span>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, computed, inject } from 'vue'
import FluentIcons from '@/components/FluentIcons.vue'
import { configAPI, systemAPI } from '@/api/index'

const theme = ref('light')
const fontSize = ref('medium')
const uptime = ref('0 天 0 小时 0 分钟 0 秒')

const settings = ref({
  systemName: 'ClassScreenLock 集控管理平台'
})

const defaultSettings = {
  systemName: 'ClassScreenLock 集控管理平台'
}

const fontSizeConfig = {
  small: { base: 12, small: 10, medium: 11, large: 14, icon: 16 },
  medium: { base: 14, small: 11, medium: 13, large: 16, icon: 18 },
  large: { base: 16, small: 13, medium: 15, large: 18, icon: 20 },
  xl: { base: 18, small: 14, medium: 16, large: 20, icon: 22 }
}

// 使用注入的通知方法
const showNotification = inject('showNotification')

const showMessage = (message, type = 'info') => {
  if (showNotification) {
    showNotification(message, type)
  }
}

const setTheme = (newTheme) => {
  theme.value = newTheme
  document.documentElement.setAttribute('data-theme', newTheme)
  localStorage.setItem('theme', newTheme)
}

const setFontSize = (size) => {
  fontSize.value = size
  const config = fontSizeConfig[size]
  const root = document.documentElement
  
  root.style.setProperty('--fui-font-size-base', `${config.base}px`)
  root.style.setProperty('--fui-font-size-small', `${config.small}px`)
  root.style.setProperty('--fui-font-size-medium', `${config.medium}px`)
  root.style.setProperty('--fui-font-size-large', `${config.large}px`)
  root.style.setProperty('--fui-icon-size', `${config.icon}px`)
  
  localStorage.setItem('fontSize', size)
  showMessage(`字体大小已设置为${size === 'small' ? '小' : size === 'medium' ? '标准' : size === 'large' ? '大' : '超大'}`, 'success')
}

const loadSettings = () => {
  const savedTheme = localStorage.getItem('theme') || 'light'
  theme.value = savedTheme
  document.documentElement.setAttribute('data-theme', savedTheme)

  const savedFontSize = localStorage.getItem('fontSize') || 'medium'
  fontSize.value = savedFontSize
  const config = fontSizeConfig[savedFontSize]
  const root = document.documentElement
  root.style.setProperty('--fui-font-size-base', `${config.base}px`)
  root.style.setProperty('--fui-font-size-small', `${config.small}px`)
  root.style.setProperty('--fui-font-size-medium', `${config.medium}px`)
  root.style.setProperty('--fui-font-size-large', `${config.large}px`)
  root.style.setProperty('--fui-icon-size', `${config.icon}px`)

  const savedSettings = localStorage.getItem('systemSettings')
  if (savedSettings) {
    const parsed = JSON.parse(savedSettings)
    settings.value = { ...settings.value, ...parsed }
  }
}

const saveSettings = async () => {
  const settingsToSave = { ...settings.value }
  localStorage.setItem('systemSettings', JSON.stringify(settingsToSave))
  
  try {
    await configAPI.update(settingsToSave)
    showMessage('系统名称已保存', 'success')
  } catch (error) {
    console.error('Failed to save backend config:', error)
    showMessage('前端设置已保存', 'info')
  }
}

const resetSettings = () => {
  settings.value = { ...defaultSettings }
  setTheme('light')
  setFontSize('medium')
  showMessage('已重置为默认设置', 'info')
}

const updateUptime = () => {
  // 从后端获取服务器启动时间
  const updateUptimeDisplay = async () => {
    try {
      const data = await systemAPI.getUptime()
      uptime.value = data.uptime
    } catch (error) {
      console.error('Failed to get uptime:', error)
    }
  }
  
  // 立即更新一次
  updateUptimeDisplay()
  
  // 每秒更新一次
  setInterval(updateUptimeDisplay, 1000)
}

onMounted(() => {
  loadSettings()
  updateUptime()
})
</script>

<style scoped>
.settings {
  padding: 20px;
  font-size: var(--fui-font-size-base);
}

.settings-container {
  max-width: 900px;
  margin: 0 auto;
}

.settings-section {
  margin-bottom: 30px;
}

.section-title {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 1.25em;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 15px;
}

.settings-card {
  background: var(--card-bg);
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.08);
  margin-bottom: 20px;
}

.setting-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px 0;
  border-bottom: 1px solid var(--border-color);
}

.setting-item:last-child {
  border-bottom: none;
}

.setting-info {
  flex: 1;
}

.setting-info h3 {
  font-size: 16px;
  font-weight: 600;
  color: var(--text-primary);
  margin-bottom: 4px;
}

.setting-info p {
  font-size: 14px;
  color: var(--text-secondary);
}

.setting-control {
  width: 300px;
}

.input-field {
  width: 100%;
  padding: 10px 14px;
  border: 1px solid var(--border-color);
  border-radius: 8px;
  font-size: 14px;
  background: var(--input-bg);
  color: var(--text-primary);
  transition: all 0.2s;
}

.input-field:focus {
  outline: none;
  border-color: var(--primary-color);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.1);
}

.theme-toggle {
  display: flex;
  gap: 12px;
}

.theme-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 24px;
  border: 2px solid var(--border-color);
  border-radius: 8px;
  background: var(--input-bg);
  color: var(--text-primary);
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
}

.theme-btn:hover {
  border-color: var(--primary-color);
  background: var(--primary-color);
  color: white;
}

.theme-btn.active {
  background: var(--primary-color);
  border-color: var(--primary-color);
  color: white;
}

.font-size-control {
  display: flex;
  gap: 10px;
}

.font-size-btn {
  padding: 10px 20px;
  border: 2px solid var(--border-color);
  border-radius: 8px;
  background: var(--input-bg);
  color: var(--text-primary);
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  min-width: 70px;
}

.font-size-btn:hover {
  border-color: var(--primary-color);
  background: var(--primary-color);
  color: white;
}

.font-size-btn.active {
  background: var(--primary-color);
  border-color: var(--primary-color);
  color: white;
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.2);
}

.info-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 0;
  border-bottom: 1px solid var(--border-color);
}

.info-item:last-child {
  border-bottom: none;
}

.info-label {
  font-size: 14px;
  color: var(--text-secondary);
}

.info-value {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}

.status-online {
  color: #10b981;
}

.settings-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
  margin-top: 20px;
}

.btn-primary,
.btn-secondary {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 12px 28px;
  border: none;
  border-radius: 8px;
  font-size: 14px;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s;
}

.btn-primary {
  background: var(--primary-color);
  color: white;
}

.btn-primary:hover {
  background: #2563eb;
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(59, 130, 246, 0.3);
}

.btn-secondary {
  background: var(--input-bg);
  color: var(--text-primary);
  border: 1px solid var(--border-color);
}

.btn-secondary:hover {
  background: var(--border-color);
}
</style>
