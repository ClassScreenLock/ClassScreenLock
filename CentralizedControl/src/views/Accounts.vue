<template>
  <div class="accounts-page">
    <div class="page-header">
      <div class="page-title">
        <FluentIcons name="person" :size="28" class="page-icon" />
        <h2>账户管理</h2>
      </div>
      <div class="header-actions">
        <div class="search-box">
          <input 
            v-model="searchQuery" 
            type="text" 
            placeholder="搜索账户" 
            class="input-field search-input"
          />
        </div>
        <div class="filter-group">
          <CustomDropdown
            v-model="filterRole"
            :options="roleOptions"
            placeholder="所有权限"
          />
        </div>
        <button @click="openAddModal" class="btn-primary">
          <FluentIcons name="add" :size="18" />
          <span>添加账户</span>
        </button>
      </div>
    </div>

    <div class="accounts-list">
      <div v-if="filteredAccounts.length === 0" class="empty-state">
        <FluentIcons name="person" :size="48" class="empty-icon" />
        <p>暂无账户数据</p>
      </div>
      <div v-else class="accounts-table">
        <table>
          <thead>
            <tr>
              <th>
                <div class="th-content">
                  <FluentIcons name="person" :size="18" class="th-icon" />
                  用户名
                </div>
              </th>
              <th>
                <div class="th-content">
                  <FluentIcons name="shield" :size="18" class="th-icon" />
                  权限
                </div>
              </th>
              <th>
                <div class="th-content">
                  <FluentIcons name="calendar" :size="18" class="th-icon" />
                  创建时间
                </div>
              </th>
              <th>
                <div class="th-content">
                  <FluentIcons name="clock" :size="18" class="th-icon" />
                  最后登录
                </div>
              </th>
              <th>
                <div class="th-content">
                  <FluentIcons name="settings" :size="18" class="th-icon" />
                  操作
                </div>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="account in filteredAccounts" :key="account.id">
              <td>
                <div class="account-name">
                  <FluentIcons name="person" :size="18" class="account-icon" />
                  {{ account.username }}
                </div>
              </td>
              <td>
                <div class="role-badge" :class="account.role">
                  <span class="role-dot" :class="account.role"></span>
                  {{ formatRole(account.role) }}
                </div>
              </td>
              <td class="time-cell">{{ formatDate(account.createdAt) }}</td>
              <td class="time-cell">{{ formatDate(account.lastLoginAt) }}</td>
              <td>
                <button class="button-icon" @click="editAccount(account)" title="编辑">
                  <FluentIcons name="edit" :size="18" />
                </button>
                <button class="button-icon" @click="showDeleteConfirm(account)" title="删除">
                  <FluentIcons name="delete" :size="18" />
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <div v-if="showModal" class="modal-overlay" @click="closeModal">
      <div class="modal-content" @click.stop>
        <div class="modal-header">
          <h3>{{ isEditing ? '编辑账户' : '添加账户' }}</h3>
          <button class="close-btn" @click="closeModal">×</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label>用户名</label>
            <input 
              v-model="formData.username" 
              type="text" 
              placeholder="请输入用户名"
              class="input-field"
            />
          </div>
          <div class="form-group">
            <label>密码</label>
            <input 
              v-model="formData.password" 
              type="password" 
              :placeholder="isEditing ? '留空则不修改密码' : '请输入密码'"
              class="input-field"
            />
          </div>
          <div class="form-group">
            <label>权限</label>
            <CustomDropdown
              v-model="formData.role"
              :options="roleOptions"
              placeholder="请选择权限"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button @click="closeModal" class="btn-secondary">取消</button>
          <button @click="saveAccount" class="btn-primary">保存</button>
        </div>
      </div>
    </div>

    <!-- 确认删除对话框 -->
    <ConfirmDialog
      v-model:visible="showConfirmDialog"
      :title="'确认删除'"
      :message="confirmMessage"
      @confirm="confirmDelete"
      @cancel="cancelDelete"
    />
  </div>
</template>

<script setup>
import { ref, computed, onMounted, inject } from 'vue'
import { accountAPI } from '@/api/account'
import CustomDropdown from '@/components/CustomDropdown.vue'
import FluentIcons from '@/components/FluentIcons.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { notify } from '@/utils/notification'

const showNotification = inject('showNotification')

const accounts = ref([])
const searchQuery = ref('')
const filterRole = ref('')
const showModal = ref(false)
const isEditing = ref(false)
const formData = ref({
  id: null,
  username: '',
  password: '',
  role: ''
})
const showConfirmDialog = ref(false)
const confirmMessage = ref('')
const currentDeleteAccount = ref(null)

const filteredAccounts = computed(() => {
  let result = accounts.value
  
  if (filterRole.value) {
    result = result.filter(a => a.role === filterRole.value)
  }
  
  if (searchQuery.value) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(a => a.username.toLowerCase().includes(query))
  }
  
  return result
})

const roleOptions = computed(() => [
  { value: '', label: '所有权限', iconName: 'list' },
  { value: 'super_admin', label: 'Super Admin', iconName: 'shield' },
  { value: 'admin', label: 'Admin', iconName: 'shield' },
  { value: 'user', label: 'User', iconName: 'person' }
])

const formatRole = (role) => {
  const roleMap = {
    'super_admin': 'Super Admin',
    'admin': 'Admin',
    'user': 'User'
  }
  return roleMap[role] || role
}

const loadAccounts = async () => {
  try {
    const data = await accountAPI.getAll()
    accounts.value = Array.isArray(data) ? data : []
  } catch (error) {
    console.error('Failed to load accounts:', error)
    accounts.value = []
  }
}

const openAddModal = () => {
  isEditing.value = false
  formData.value = {
    id: null,
    username: '',
    password: '',
    role: ''
  }
  showModal.value = true
}

const editAccount = (account) => {
  isEditing.value = true
  formData.value = {
    id: account.id,
    username: account.username,
    password: '',
    role: account.role
  }
  showModal.value = true
}

const saveAccount = async () => {
  try {
    if (isEditing.value) {
      await accountAPI.update(formData.value.id, {
        username: formData.value.username,
        password: formData.value.password,
        role: formData.value.role
      })
    } else {
      await accountAPI.create({
        username: formData.value.username,
        password: formData.value.password,
        role: formData.value.role
      })
    }
    await loadAccounts()
    closeModal()
  } catch (error) {
    console.error('Failed to save account:', error)
    if (showNotification) {
      showNotification('保存失败：' + error.message, 'error')
    } else {
      notify.error('保存失败：' + error.message)
    }
  }
}

const showDeleteConfirm = (account) => {
  currentDeleteAccount.value = account
  confirmMessage.value = `确定要删除账户 "${account.username}" 吗？`
  showConfirmDialog.value = true
}

const confirmDelete = async () => {
  if (currentDeleteAccount.value) {
    try {
      await accountAPI.delete(currentDeleteAccount.value.id)
      await loadAccounts()
      if (showNotification) {
        showNotification('账户删除成功', 'success')
      } else {
        notify.success('账户删除成功')
      }
    } catch (error) {
      console.error('Failed to delete account:', error)
      if (showNotification) {
        showNotification('删除失败：' + error.message, 'error')
      } else {
        notify.error('删除失败：' + error.message)
      }
    } finally {
      showConfirmDialog.value = false
      currentDeleteAccount.value = null
    }
  }
}

const cancelDelete = () => {
  showConfirmDialog.value = false
  currentDeleteAccount.value = null
}

const closeModal = () => {
  showModal.value = false
  formData.value = {
    id: null,
    username: '',
    password: '',
    role: ''
  }
}

const formatDate = (date) => {
  if (!date) return '从未登录'
  const d = new Date(date)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

onMounted(() => {
  loadAccounts()
})
</script>

<style scoped>
.accounts-page {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 24px;
  background: rgba(255, 255, 255, 0.05);
  min-height: 100vh;
  animation: pageFadeIn 0.5s ease-out;
}

@keyframes pageFadeIn {
  from {
    opacity: 0;
    transform: translateY(10px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 4px;
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

.header-actions {
  display: flex;
  gap: 10px;
  align-items: center;
}

.search-box {
  display: flex;
  align-items: center;
}

.search-input {
  width: 280px;
  height: 40px;
  padding: 0 14px;
  background: rgba(255, 255, 255, 0.04);
  border: 1.5px solid rgba(255, 255, 255, 0.15);
  border-radius: 40px;
  color: var(--fui-text);
  font-size: 1em;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  outline: none;
}

.search-input:hover {
  background: rgba(255, 255, 255, 0.06);
  border-color: rgba(255, 255, 255, 0.2);
}

.search-input:focus {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.3);
  box-shadow: 0 0 0 3px rgba(255, 255, 255, 0.08);
}

.search-input::placeholder {
  color: var(--fui-text-secondary);
  opacity: 0.6;
}

.filter-group {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.filter-group :deep(.custom-dropdown) {
  min-width: 180px;
  flex: 1;
}

.btn-primary,
.btn-secondary {
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

.accounts-list {
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(0, 0, 0, 0.2);
  border-radius: 8px;
  overflow: hidden;
  animation: containerFadeIn 0.4s ease-out 0.1s both;
  padding: 3px;
  font-size: var(--fui-font-size-base);
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

.empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 20px;
  color: var(--fui-text-secondary);
  gap: 16px;
}

.empty-icon {
  opacity: 0.4;
}

.empty-state p {
  margin: 0;
  font-size: 0.9375em;
  opacity: 0.8;
}

.accounts-table {
  width: 100%;
  overflow-x: auto;
}

table {
  width: 100%;
  border-spacing: 0;
  border: 1.5px solid rgba(255, 255, 255, 0.2);
  border-radius: 8px;
  overflow: hidden;
}

thead th {
  text-align: left;
  padding: 12px 16px;
  background: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
  color: var(--fui-text);
  font-weight: 600;
  font-size: 0.75em;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.th-content {
  display: flex;
  align-items: center;
  gap: 8px;
}

.th-icon {
  opacity: 0.7;
}

tbody td {
  padding: 12px 16px;
  border-right: 1px solid rgba(255, 255, 255, 0.1);
  color: var(--fui-text);
  font-size: 0.875em;
  vertical-align: middle;
}

tbody td:last-child {
  border-right: none;
}

tbody tr {
  border-bottom: 1px solid rgba(255, 255, 255, 0.15);
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  opacity: 0;
  animation: rowFadeIn 0.3s ease-out both;
  border-radius: 4px;
}

tbody tr:nth-child(even) {
  background: rgba(255, 255, 255, 0.02);
}

tbody tr:last-child td:first-child {
  border-bottom-left-radius: 6px;
}

tbody tr:last-child td:last-child {
  border-bottom-right-radius: 6px;
}

tbody tr:nth-child(1) { animation-delay: 0.03s; }
tbody tr:nth-child(2) { animation-delay: 0.06s; }
tbody tr:nth-child(3) { animation-delay: 0.09s; }
tbody tr:nth-child(4) { animation-delay: 0.12s; }
tbody tr:nth-child(5) { animation-delay: 0.15s; }

@keyframes rowFadeIn {
  from {
    opacity: 0;
    transform: translateY(2px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

tbody tr:hover {
  background-color: rgba(255, 255, 255, 0.05);
  border-radius: 4px;
  transform: translateX(2px);
}

.account-name {
  display: flex;
  align-items: center;
  gap: 10px;
  font-weight: 500;
}

.account-icon {
  opacity: 0.7;
  flex-shrink: 0;
}

.role-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 5px 12px;
  border-radius: 99px;
  font-size: 0.75em;
  font-weight: 600;
  letter-spacing: 0.2px;
  transition: all 0.2s ease;
}

.role-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
  position: relative;
}

.role-dot.super_admin {
  background: linear-gradient(135deg, #0099ff 0%, #0066cc 100%);
  box-shadow: 0 0 0 2px rgba(0, 153, 255, 0.15), 0 0 8px rgba(0, 153, 255, 0.4);
}

.role-dot.admin {
  background: linear-gradient(135deg, #10b981 0%, #059669 100%);
  box-shadow: 0 0 0 2px rgba(16, 185, 129, 0.15), 0 0 8px rgba(16, 185, 129, 0.4);
}

.role-dot.user {
  background: linear-gradient(135deg, #9ca3af 0%, #6b7280 100%);
  box-shadow: 0 0 0 2px rgba(156, 163, 175, 0.15), 0 0 8px rgba(156, 163, 175, 0.3);
}

.role-badge.super_admin {
  background: linear-gradient(135deg, rgba(0, 153, 255, 0.12) 0%, rgba(0, 102, 204, 0.08) 100%);
  color: #0099ff;
  border: 1px solid rgba(0, 153, 255, 0.2);
}

.role-badge.admin {
  background: linear-gradient(135deg, rgba(16, 185, 129, 0.12) 0%, rgba(5, 150, 105, 0.08) 100%);
  color: #10b981;
  border: 1px solid rgba(16, 185, 129, 0.2);
}

.role-badge.user {
  background: linear-gradient(135deg, rgba(156, 163, 175, 0.12) 0%, rgba(107, 114, 128, 0.08) 100%);
  color: #6b7280;
  border: 1px solid rgba(156, 163, 175, 0.2);
}

.time-cell {
  color: var(--fui-text-secondary);
  font-size: 0.8125em;
}

.button-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 34px;
  height: 34px;
  padding: 0;
  background: rgba(255, 255, 255, 0.03);
  border: 1px solid rgba(255, 255, 255, 0.08);
  border-radius: 8px;
  color: var(--fui-text);
  cursor: pointer;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  margin-right: 4px;
}

.button-icon:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.15);
  transform: scale(1.05);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

.button-icon:active {
  transform: scale(0.98);
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
  backdrop-filter: blur(4px);
}

.modal-content {
  background: var(--fui-surface);
  border-radius: 12px;
  width: 90%;
  max-width: 500px;
  max-height: 90vh;
  overflow-y: auto;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
  animation: modalSlideIn 0.3s ease-out;
}

@keyframes modalSlideIn {
  from {
    opacity: 0;
    transform: translateY(-20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px 24px;
  border-bottom: 1px solid var(--fui-border);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.125em;
  font-weight: 600;
  color: var(--fui-text);
}

.close-btn {
  background: none;
  border: none;
  font-size: 1.75em;
  color: var(--fui-text-secondary);
  cursor: pointer;
  padding: 0;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  transition: all 0.2s;
}

.close-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--fui-text);
}

.modal-body {
  padding: 24px;
}

.form-group {
  margin-bottom: 20px;
}

.form-group label {
  display: block;
  margin-bottom: 8px;
  font-size: 0.875em;
  font-weight: 500;
  color: var(--fui-text);
}

.form-group .input-field {
  width: 100%;
  height: 40px;
  padding: 0 14px;
  background: rgba(255, 255, 255, 0.04);
  border: 1.5px solid rgba(255, 255, 255, 0.15);
  border-radius: 8px;
  color: var(--fui-text);
  font-size: 1em;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  outline: none;
}

.form-group .input-field:hover {
  background: rgba(255, 255, 255, 0.06);
  border-color: rgba(255, 255, 255, 0.2);
}

.form-group .input-field:focus {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.3);
  box-shadow: 0 0 0 3px rgba(255, 255, 255, 0.08);
}

.form-group .input-field::placeholder {
  color: var(--fui-text-secondary);
  opacity: 0.6;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 20px 24px;
  border-top: 1px solid var(--fui-border);
}
</style>