// 通知工具函数
// 用于在全局任何地方显示通知

let notificationHandler = null

// 设置通知处理器
export const setNotificationHandler = (handler) => {
  notificationHandler = handler
}

// 显示通知
export const showNotification = (message, type = 'info', title = null) => {
  if (notificationHandler) {
    notificationHandler(message, type, title)
  } else {
    // 如果没有设置处理器，回退到 console.log
    console.log(`[${type.toUpperCase()}] ${title || ''}: ${message}`)
  }
}

// 快捷方法
export const notify = {
  success: (message, title) => showNotification(message, 'success', title || '成功'),
  error: (message, title) => showNotification(message, 'error', title || '错误'),
  warning: (message, title) => showNotification(message, 'warning', title || '警告'),
  info: (message, title) => showNotification(message, 'info', title || '提示')
}
