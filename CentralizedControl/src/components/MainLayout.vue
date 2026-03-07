<template>
  <div class="app-container">
    <nav class="sidebar">
      <div class="sidebar-header">
        <h2>ClassScreenLock</h2>
        <p>集控管理平台</p>
      </div>
      <ul class="nav-menu">
        <li v-for="item in menuItems" :key="item.path">
          <router-link :to="item.path" class="nav-link" :class="{ active: isActive(item.path) }">
            <FluentIcons :name="item.icon" :size="20" />
            <span>{{ item.title }}</span>
          </router-link>
        </li>
      </ul>
      <div class="sidebar-footer">
        <div class="user-info" v-if="user">
          <span class="username">{{ user.username }}</span>
          <span class="role-badge" :class="user.role">{{ formatRole(user.role) }}</span>
        </div>
        <button @click="handleLogout" class="logout-button">
          <FluentIcons name="logout" :size="18" />
          <span>退出登录</span>
        </button>
      </div>
    </nav>
    <main class="main-content">
      <header class="top-bar">
        <h1>{{ currentTitle }}</h1>
      </header>
      <div class="content">
        <router-view />
      </div>
    </main>
    <Notification ref="notificationRef" />
  </div>
</template>

<script setup>
import { computed, ref, onMounted, provide } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { authAPI } from '@/api/auth'
import FluentIcons from './FluentIcons.vue'
import Notification from './Notification.vue'
import { setNotificationHandler } from '@/utils/notification'

const route = useRoute()
const router = useRouter()
const user = ref(null)
const notificationRef = ref(null)

// 设置全局通知处理器
onMounted(() => {
  setNotificationHandler((message, type, title) => {
    if (notificationRef.value) {
      notificationRef.value.showNotification(message, type, title)
    }
  })
})

// 提供通知方法给所有子组件
provide('showNotification', (message, type = 'info') => {
  if (notificationRef.value) {
    notificationRef.value.showNotification(message, type)
  }
})

const menuItems = [
  { path: '/dashboard', title: '仪表盘', icon: 'dashboard' },
  { path: '/organizations', title: '组织管理', icon: 'organization' },
  { path: '/devices', title: '设备管理', icon: 'device' },
  { path: '/accounts', title: '账户管理', icon: 'person' },
  { path: '/security-config', title: '安全中心配置', icon: 'security' },
  { path: '/network-intercept', title: '网络拦截配置', icon: 'network' },
  { path: '/permissions', title: '权限管理', icon: 'shield' },
  { path: '/settings', title: '系统设置', icon: 'settings' }
]

const currentTitle = computed(() => route.meta.title || 'Dashboard')

const isActive = (path) => route.path === path

// 加载用户信息
onMounted(() => {
  const userData = localStorage.getItem('user')
  if (userData) {
    user.value = JSON.parse(userData)
  }
})

// 格式化角色显示
const formatRole = (role) => {
  const roleMap = {
    'super_admin': '超级管理员',
    'admin': '管理员',
    'user': '用户'
  }
  return roleMap[role] || role
}

// 退出登录
const handleLogout = async () => {
  try {
    await authAPI.logout()
  } catch (err) {
    console.error('登出失败:', err)
  } finally {
    localStorage.removeItem('user')
    router.push('/login')
  }
}
</script>

<style scoped>
.app-container {
  display: flex;
  height: 100vh;
  overflow: hidden;
}

.sidebar {
  width: 280px;
  background: var(--fui-surface);
  border-right: 1px solid var(--fui-border);
  display: flex;
  flex-direction: column;
  overflow-y: auto;
}

.sidebar-header {
  padding: 24px 20px;
  border-bottom: 1px solid var(--fui-border);
}

.sidebar-header h2 {
  font-size: 26px;
  font-weight: 700;
  color: var(--fui-primary);
  margin-bottom: 6px;
  letter-spacing: 0.3px;
  word-break: break-word;
}

.sidebar-header p {
  font-size: 13px;
  color: var(--fui-text-secondary);
  margin: 0;
  font-weight: 500;
}

.nav-menu {
  list-style: none;
  padding: 16px 0;
  flex: 1;
}

.sidebar-footer {
  padding: 16px;
  border-top: 1px solid var(--fui-border);
  background: var(--fui-background);
}

.user-info {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 12px;
  margin-bottom: 8px;
  background: var(--fui-surface);
  border-radius: var(--fui-radius);
}

.username {
  font-size: var(--fui-font-size-small);
  font-weight: 500;
  color: var(--fui-text);
}

.role-badge {
  font-size: 11px;
  padding: 2px 8px;
  border-radius: 10px;
}

.role-badge.super_admin {
  background: linear-gradient(135deg, #0099ff 0%, #0066cc 100%);
  color: white;
  font-weight: 600;
  box-shadow: 0 2px 8px rgba(0, 153, 255, 0.3);
}

.role-badge.admin {
  background: var(--fui-primary);
  color: white;
}

.role-badge.user {
  background: var(--neutral-fill-strong);
  color: white;
}

.logout-button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 100%;
  padding: 10px;
  background: transparent;
  border: 1px solid var(--fui-border);
  border-radius: var(--fui-radius);
  color: var(--fui-text);
  font-size: var(--fui-font-size-small);
  cursor: pointer;
  transition: all 0.2s;
}

.logout-button:hover {
  background: var(--error-fill-default);
  border-color: var(--error-stroke-default);
  color: var(--error-foreground-rest);
}

.logout-button :deep(.fluent-icon) {
  margin-right: 8px;
}

.nav-menu li {
  margin: 4px 12px;
}

.nav-link {
  display: flex;
  align-items: center;
  padding: 12px 16px;
  text-decoration: none;
  color: var(--fui-text);
  border-radius: var(--fui-radius);
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  font-size: var(--fui-font-size-medium);
  opacity: 0.8;
  transform: translateX(-10px);
  animation: navLinkFadeIn 0.4s cubic-bezier(0.4, 0, 0.2, 1);
  animation-fill-mode: both;
}

.nav-menu li:nth-child(1) .nav-link { animation-delay: 0.1s; }
.nav-menu li:nth-child(2) .nav-link { animation-delay: 0.2s; }
.nav-menu li:nth-child(3) .nav-link { animation-delay: 0.3s; }
.nav-menu li:nth-child(4) .nav-link { animation-delay: 0.4s; }
.nav-menu li:nth-child(5) .nav-link { animation-delay: 0.5s; }
.nav-menu li:nth-child(6) .nav-link { animation-delay: 0.6s; }
.nav-menu li:nth-child(7) .nav-link { animation-delay: 0.7s; }

@keyframes navLinkFadeIn {
  from {
    opacity: 0;
    transform: translateX(-10px);
  }
  to {
    opacity: 0.8;
    transform: translateX(0);
  }
}

.nav-link:hover {
  background-color: var(--fui-background);
  opacity: 1;
  transform: translateX(4px);
}

.nav-link.active {
  background-color: var(--fui-primary);
  color: white;
}

.nav-link :deep(.fluent-icon) {
  margin-right: 12px;
}

.main-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background-color: var(--fui-background);
  font-size: var(--fui-font-size-base);
}

.top-bar {
  background: var(--fui-surface);
  border-bottom: 1px solid var(--fui-border);
  padding: 20px 32px;
  box-shadow: var(--fui-shadow);
}

.top-bar h1 {
  font-size: 1.5em;
  font-weight: 600;
  margin: 0;
  color: var(--fui-text);
}

.content {
  flex: 1;
  overflow-y: auto;
  padding: 24px 32px;
  font-size: var(--fui-font-size-base);
}
</style>
