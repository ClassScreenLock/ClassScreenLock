<template>
  <Teleport to="body">
    <TransitionGroup name="notification-list" tag="div" class="notification-container">
      <div 
        v-for="notification in notifications" 
        :key="notification.id"
        :class="['notification', `notification-${notification.type}`]"
      >
        <div class="notification-icon">
          <FluentIcons 
            :name="getNotificationIcon(notification.type)" 
            :size="20" 
          />
        </div>
        <div class="notification-content">
          <div class="notification-title">{{ notification.title }}</div>
          <div class="notification-message">{{ notification.message }}</div>
        </div>
        <button class="notification-close" @click="closeNotification(notification.id)">
          <FluentIcons name="close" :size="16" />
        </button>
      </div>
    </TransitionGroup>
  </Teleport>
</template>

<script setup>
import { ref } from 'vue'
import FluentIcons from './FluentIcons.vue'

const notifications = ref([])
let notificationId = 0

const getNotificationIcon = (type) => {
  const iconMap = {
    success: 'check_circle',
    error: 'error',
    warning: 'warning',
    info: 'info'
  }
  return iconMap[type] || 'info'
}

const showNotification = (message, type = 'info', title = null) => {
  const id = ++notificationId
  const defaultTitles = {
    success: '成功',
    error: '错误',
    warning: '警告',
    info: '提示'
  }
  
  notifications.value.push({
    id,
    type,
    title: title || defaultTitles[type],
    message
  })
  
  // 5 秒后自动关闭
  setTimeout(() => {
    closeNotification(id)
  }, 5000)
}

const closeNotification = (id) => {
  const index = notifications.value.findIndex(n => n.id === id)
  if (index > -1) {
    notifications.value.splice(index, 1)
  }
}

// 暴露方法给外部调用
defineExpose({
  showNotification,
  success: (message, title) => showNotification(message, 'success', title),
  error: (message, title) => showNotification(message, 'error', title),
  warning: (message, title) => showNotification(message, 'warning', title),
  info: (message, title) => showNotification(message, 'info', title)
})
</script>

<style>
.notification-container {
  position: fixed;
  top: 20px;
  right: 20px;
  z-index: 9999;
  display: flex;
  flex-direction: column;
  gap: 16px;
  max-width: 550px;
  min-width: 400px;
}

.notification {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  padding: 20px 18px;
  border-radius: 8px;
  box-shadow: 0 6px 16px rgba(0, 0, 0, 0.15);
  animation: slideInRight 0.3s ease-out;
  background: #ffffff;
  border-left: 4px solid;
  min-width: 400px;
}

[data-theme='dark'] .notification {
  background: #2d2d2d;
  box-shadow: 0 6px 16px rgba(0, 0, 0, 0.5);
}

.notification-success {
  border-left-color: #10b981;
}

.notification-error {
  border-left-color: #ef4444;
}

.notification-warning {
  border-left-color: #f59e0b;
}

.notification-info {
  border-left-color: #3b82f6;
}

.notification-icon {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  color: #323130;
}

[data-theme='dark'] .notification-icon {
  color: #ffffff;
}

.notification-success .notification-icon {
  color: #10b981;
}

.notification-error .notification-icon {
  color: #ef4444;
}

.notification-warning .notification-icon {
  color: #f59e0b;
}

.notification-info .notification-icon {
  color: #3b82f6;
}

.notification-content {
  flex: 1;
  min-width: 0;
}

.notification-title {
  font-weight: 600;
  font-size: 16px;
  color: #323130;
  margin-bottom: 6px;
}

[data-theme='dark'] .notification-title {
  color: #ffffff;
}

.notification-message {
  font-size: 15px;
  color: #605e5c;
  line-height: 1.5;
  word-wrap: break-word;
}

[data-theme='dark'] .notification-message {
  color: #b0b0b0;
}

.notification-close {
  flex-shrink: 0;
  background: none;
  border: none;
  cursor: pointer;
  color: #605e5c;
  padding: 4px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  transition: all 0.2s;
}

.notification-close:hover {
  background: rgba(0, 0, 0, 0.05);
  color: #323130;
}

[data-theme='dark'] .notification-close {
  color: #b0b0b0;
}

[data-theme='dark'] .notification-close:hover {
  background: rgba(255, 255, 255, 0.1);
  color: #ffffff;
}

/* 动画效果 */
.notification-list-enter-active,
.notification-list-leave-active {
  transition: all 0.3s ease;
}

.notification-list-enter-from {
  opacity: 0;
  transform: translateX(100%);
}

.notification-list-leave-to {
  opacity: 0;
  transform: translateX(100%);
}

@keyframes slideInRight {
  from {
    opacity: 0;
    transform: translateX(100%);
  }
  to {
    opacity: 1;
    transform: translateX(0);
  }
}
</style>
