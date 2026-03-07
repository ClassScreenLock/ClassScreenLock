<template>
  <div class="login-container">
    <ParticleBackground />
    
    <div class="login-card">
      <div class="login-header">
        <h1>ClassScreenLock</h1>
        <p>集控管理平台</p>
      </div>
      
      <div class="login-body">
        <form @submit.prevent="handleLogin">
          <div class="form-group">
            <label>用户名</label>
            <div class="input-wrapper">
              <span class="input-icon">👤</span>
              <input 
                type="text" 
                v-model="username" 
                class="input-field" 
                placeholder="请输入用户名"
                required
                autocomplete="off"
              >
            </div>
          </div>
          
          <div class="form-group">
            <label>密码</label>
            <div class="input-wrapper">
              <span class="input-icon">🔑</span>
              <input 
                type="password" 
                v-model="password" 
                class="input-field" 
                placeholder="请输入密码"
                required
                autocomplete="off"
              >
            </div>
          </div>
          
          <div class="form-group" v-if="error">
            <div class="error-message">{{ error }}</div>
          </div>
          
          <button type="submit" class="button-primary button-block" :disabled="loading">
            <span v-if="!loading" class="button-text">登录</span>
            <span v-else class="loading-spinner">
              <span class="spinner"></span>
              <span>登录中...</span>
            </span>
          </button>
        </form>
        
        <div class="login-footer">
          <p class="hint">
            <span class="hint-icon">💡</span>
            <span>请使用您的账户和密码登录</span>
          </p>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { authAPI } from '@/api/auth'
import ParticleBackground from '@/components/ParticleBackground.vue'

const router = useRouter()
const username = ref('')
const password = ref('')
const error = ref('')
const loading = ref(false)

const handleLogin = async () => {
  if (!username.value || !password.value) {
    error.value = '请输入用户名和密码'
    return
  }
  
  loading.value = true
  error.value = ''
  
  try {
    const user = await authAPI.login({
      username: username.value,
      password: password.value
    })
    
    // 清除旧的登录信息
    localStorage.removeItem('user')
    localStorage.removeItem('tokenExpiresAt')
    
    // 保存新的用户信息到 localStorage
    localStorage.setItem('user', JSON.stringify(user))
    // 保存 token 过期时间
    if (user.expiresAt) {
      localStorage.setItem('tokenExpiresAt', user.expiresAt)
    }
    
    // 强制刷新路由
    await router.push('/dashboard')
  } catch (err) {
    console.error('登录失败:', err)
    error.value = err.response?.data?.error || '登录失败，请检查用户名和密码'
  } finally {
    loading.value = false
  }
}
</script>

<style scoped>
.login-container {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, #003366 0%, #0066cc 50%, #0099ff 100%);
  position: relative;
  overflow: hidden;
}

[data-theme='dark'] .login-container {
  background: linear-gradient(135deg, #0a1929 0%, #1a3a5c 50%, #2a5a8c 100%);
}

/* 简洁的卡片样式 */
.login-card {
  background: rgba(255, 255, 255, 0.95);
  border-radius: calc(var(--layer-corner-radius) * 2);
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
  padding: 40px;
  width: 100%;
  max-width: 420px;
  position: relative;
  z-index: 1;
  backdrop-filter: blur(10px);
  font-size: var(--fui-font-size-base);
}

[data-theme='dark'] .login-card {
  background: rgba(30, 30, 30, 0.95);
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.5);
}

/* Logo 样式 */
.login-header {
  text-align: center;
  margin-bottom: 30px;
}

/* 标题样式 */
.login-header h1 {
  font-size: 28px;
  font-weight: 600;
  color: var(--neutral-foreground-rest);
  margin: 0 0 8px 0;
  text-align: center;
}

.login-header p {
  font-size: 14px;
  color: var(--neutral-foreground-secondary);
  margin: 0;
  text-align: center;
}

[data-theme='dark'] .login-header h1 {
  color: #ffffff;
}

[data-theme='dark'] .login-header p {
  color: #b0b0b0;
}

/* 表单样式 */
.form-group {
  margin-bottom: 20px;
}

.form-group label {
  display: block;
  font-size: 14px;
  font-weight: 500;
  color: var(--neutral-foreground-rest);
  margin-bottom: 8px;
}

[data-theme='dark'] .form-group label {
  color: #e0e0e0;
}

/* 输入框包装器 */
.input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.input-icon {
  position: absolute;
  left: 12px;
  font-size: 18px;
  color: var(--neutral-foreground-secondary);
  z-index: 1;
}

.input-field {
  width: 100%;
  padding: 12px 12px 12px 40px;
  font-size: 14px;
  border: 2px solid var(--stroke-outline-default);
  border-radius: var(--layer-corner-radius);
  background: var(--fill-layer-default);
  color: var(--neutral-foreground-rest);
  transition: all 0.3s;
}

.input-field:focus {
  outline: none;
  border-color: #0099ff;
  box-shadow: 0 0 0 3px rgba(0, 153, 255, 0.2);
}

[data-theme='dark'] .input-field {
  background: rgba(45, 45, 45, 0.8);
  border-color: #404040;
  color: #ffffff;
}

[data-theme='dark'] .input-field::placeholder {
  color: #808080;
}

[data-theme='dark'] .input-field:focus {
  border-color: #0099ff;
  box-shadow: 0 0 0 3px rgba(0, 153, 255, 0.3);
}

.input-field:focus + .input-icon,
.input-wrapper:focus-within .input-icon {
  color: #0099ff;
}

/* 错误消息样式 */
.error-message {
  padding: 10px 14px;
  background: rgba(245, 87, 108, 0.1);
  color: #f5576c;
  border-radius: var(--layer-corner-radius);
  font-size: 13px;
  border-left: 3px solid #f5576c;
  margin-top: 10px;
}

[data-theme='dark'] .error-message {
  background: rgba(245, 87, 108, 0.2);
  color: #ff6b7a;
  border-left-color: #ff6b7a;
}

/* 按钮样式 */
.button-primary {
  width: 100%;
  padding: 12px;
  font-size: 14px;
  font-weight: 600;
  background: linear-gradient(135deg, #0099ff 0%, #0066cc 100%);
  border: none;
  border-radius: var(--layer-corner-radius);
  color: white;
  cursor: pointer;
  transition: all 0.3s;
}

.button-primary:hover:not(:disabled) {
  transform: translateY(-2px);
  box-shadow: 0 8px 20px rgba(0, 153, 255, 0.4);
}

.button-primary:active:not(:disabled) {
  transform: translateY(0);
}

.button-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* 加载动画 */
.loading-spinner {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.spinner {
  width: 16px;
  height: 16px;
  border: 2px solid rgba(255, 255, 255, 0.3);
  border-top-color: white;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

/* 底部提示 */
.login-footer {
  margin-top: 20px;
  text-align: center;
}

.hint {
  font-size: 12px;
  color: var(--neutral-foreground-secondary);
  margin: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
}

.hint-icon {
  font-size: 14px;
}

[data-theme='dark'] .hint {
  color: #b0b0b0;
}
</style>
