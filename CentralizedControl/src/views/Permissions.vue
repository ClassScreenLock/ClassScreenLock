<template>
  <div class="permission-manager">
    <div class="page-header">
      <div class="page-title">
        <FluentIcons name="shield" :size="28" class="page-icon" />
        <h2>权限管理</h2>
      </div>
    </div>

    <div class="permission-container">
      <div class="permission-card">
        <h3>页面访问权限</h3>
        <p class="card-description">设置不同角色可以访问的页面</p>
        
        <div class="permission-table">
          <table>
            <thead>
              <tr>
                <th>页面</th>
                <th>Super Admin</th>
                <th>Admin</th>
                <th>User</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="page in pages" :key="page.path">
                <td class="page-name">
                  <FluentIcons :name="page.icon" :size="16" class="page-icon" />
                  {{ page.title }}
                </td>
                <td>
                  <FluentToggle 
                    :checked="permissions[page.path]?.includes('super_admin')"
                    :disabled="true"
                  />
                </td>
                <td>
                  <FluentToggle 
                    :checked="permissions[page.path]?.includes('admin')"
                    @update:checked="(value) => togglePermission(page.path, 'admin', value)"
                  />
                </td>
                <td>
                  <FluentToggle 
                    :checked="permissions[page.path]?.includes('user')"
                    @update:checked="(value) => togglePermission(page.path, 'user', value)"
                  />
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <div class="permission-actions">
        <button @click="savePermissions" class="btn-primary">
          <FluentIcons name="save" :size="18" />
          <span>保存权限设置</span>
        </button>
        <button @click="resetPermissions" class="btn-secondary">
          <FluentIcons name="refresh" :size="18" />
          <span>重置默认</span>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted, inject } from 'vue'
import FluentIcons from '@/components/FluentIcons.vue'
import FluentToggle from '@/components/FluentToggle.vue'
import { notify } from '@/utils/notification'

const showNotification = inject('showNotification')

const pages = [
  { path: '/dashboard', title: '仪表盘', icon: 'dashboard' },
  { path: '/organizations', title: '组织管理', icon: 'organization' },
  { path: '/devices', title: '设备管理', icon: 'device' },
  { path: '/accounts', title: '账户管理', icon: 'person' },
  { path: '/security-config', title: '安全中心配置', icon: 'security' },
  { path: '/network-intercept', title: '网络拦截配置', icon: 'network' },
  { path: '/settings', title: '系统设置', icon: 'settings' },
  { path: '/permissions', title: '权限管理', icon: 'shield' }
]

const defaultPermissions = {
  '/dashboard': ['super_admin', 'admin', 'user'],
  '/organizations': ['super_admin', 'admin'],
  '/devices': ['super_admin', 'admin'],
  '/accounts': ['super_admin', 'admin'],
  '/security-config': ['super_admin', 'admin'],
  '/network-intercept': ['super_admin', 'admin'],
  '/settings': ['super_admin'],
  '/permissions': ['super_admin']
}

const permissions = ref({ ...defaultPermissions })

const loadPermissions = () => {
  const savedPermissions = localStorage.getItem('permissions')
  if (savedPermissions) {
    try {
      permissions.value = JSON.parse(savedPermissions)
    } catch (error) {
      console.error('Failed to load permissions:', error)
      permissions.value = { ...defaultPermissions }
    }
  }
}

const savePermissions = () => {
  localStorage.setItem('permissions', JSON.stringify(permissions.value))
  if (showNotification) {
    showNotification('权限设置已保存', 'success')
  } else {
    notify.success('权限设置已保存')
  }
}

const resetPermissions = () => {
  permissions.value = { ...defaultPermissions }
  if (showNotification) {
    showNotification('已重置为默认权限设置', 'info')
  } else {
    notify.info('已重置为默认权限设置')
  }
}

const togglePermission = (path, role, value) => {
  if (!permissions.value[path]) {
    permissions.value[path] = []
  }
  
  const index = permissions.value[path].indexOf(role)
  if (value && index === -1) {
    permissions.value[path].push(role)
  } else if (!value && index > -1) {
    permissions.value[path].splice(index, 1)
  }
}

onMounted(() => {
  loadPermissions()
})
</script>

<style scoped>
.permission-manager {
  padding: 24px;
  background: rgba(255, 255, 255, 0.05);
  min-height: 100vh;
  font-size: var(--fui-font-size-base);
}

.page-header {
  margin-bottom: 30px;
}

.page-title {
  display: flex;
  align-items: center;
  gap: 12px;
}

.page-icon {
  color: var(--fui-text);
  opacity: 0.9;
}

.page-title h2 {
  margin: 0;
  font-size: 1.25em;
  font-weight: 600;
  color: var(--fui-text);
  letter-spacing: -0.3px;
}

.permission-container {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.permission-card {
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(0, 0, 0, 0.2);
  border-radius: 8px;
  padding: 24px;
  animation: containerFadeIn 0.4s ease-out 0.1s both;
}

@keyframes containerFadeIn {
  from {
    opacity: 0;
    transform: translateY(8px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.permission-card h3 {
  margin: 0 0 8px 0;
  font-size: 1.125em;
  font-weight: 600;
  color: var(--fui-text);
}

.card-description {
  margin: 0 0 20px 0;
  font-size: 0.875em;
  color: var(--fui-text-secondary);
  opacity: 0.8;
}

.permission-table {
  overflow-x: auto;
}

table {
  width: 100%;
  border-spacing: 0;
  border: 1.5px solid rgba(255, 255, 255, 0.2);
  border-radius: 8px;
  overflow: hidden;
}

th, td {
  padding: 12px 16px;
  text-align: left;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  border-right: 1px solid rgba(255, 255, 255, 0.1);
}

th {
  background: rgba(255, 255, 255, 0.1);
  color: var(--fui-text);
  font-weight: 600;
  font-size: 0.75em;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

td {
  color: var(--fui-text);
  font-size: 0.875em;
}

th:last-child, td:last-child {
  border-right: none;
}

tr:last-child td {
  border-bottom: none;
}

.page-name {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 500;
}

.page-name .page-icon {
  opacity: 0.7;
  flex-shrink: 0;
}

input[type="checkbox"] {
  width: 18px;
  height: 18px;
  cursor: pointer;
}

input[type="checkbox"]:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.permission-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
  margin-top: 10px;
}

.btn-primary, .btn-secondary {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  padding: 0 20px;
  height: 40px;
  border: none;
  border-radius: 8px;
  font-size: 1em;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.btn-primary {
  background: var(--fui-primary);
  color: white;
}

.btn-primary:hover {
  background: var(--fui-primary-hover);
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(59, 130, 246, 0.3);
}

.btn-secondary {
  background: rgba(255, 255, 255, 0.04);
  color: var(--fui-text);
  border: 1px solid rgba(255, 255, 255, 0.15);
}

.btn-secondary:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.2);
}
</style>