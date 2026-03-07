<template>
  <div class="devices-page">
    <div class="page-header">
      <div class="page-title">
        <FluentIcons name="device" :size="28" class="page-icon" />
        <h2>设备管理</h2>
      </div>
      <div class="header-actions">
        <div class="search-box">
          <input 
            v-model="searchQuery" 
            type="text" 
            placeholder="搜索设备" 
            class="input-field search-input"
          />
        </div>
        <div class="filter-group">
          <CustomDropdown
            v-model="filterStatus"
            :options="statusOptions"
            placeholder="所有状态"
          />
          <CustomDropdown
            v-model="filterOrg"
            :options="orgOptions"
            placeholder="所有组织"
          />
        </div>
      </div>
    </div>

    <div class="devices-list">
      <div v-if="filteredDevices.length === 0" class="empty-state">
        <FluentIcons name="device" :size="48" class="empty-icon" />
        <p>暂无设备数据</p>
      </div>
      <div v-else class="devices-table">
        <table>
          <thead>
            <tr>
              <th>
                <div class="th-content">
                  <FluentIcons name="computer" :size="18" class="th-icon" />
                  设备名称
                </div>
              </th>
              <th>
                <div class="th-content">
                  <FluentIcons name="globe" :size="18" class="th-icon" />
                  IP 地址
                </div>
              </th>
              <th>
                <div class="th-content">
                  <FluentIcons name="building" :size="18" class="th-icon" />
                  所属组织
                </div>
              </th>
              <th>
                <div class="th-content">
                  <FluentIcons name="check_circle" :size="18" class="th-icon" />
                  状态
                </div>
              </th>
              <th>
                <div class="th-content">
                  <FluentIcons name="clock" :size="18" class="th-icon" />
                  最后在线
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
            <tr v-for="device in filteredDevices" :key="device.id">
              <td>
                <div class="device-name">
                  <FluentIcons name="computer" :size="18" class="device-icon" />
                  {{ device.name }}
                </div>
              </td>
              <td>{{ device.ipAddress }}</td>
              <td>
                <div class="org-name">
                  <FluentIcons name="building" :size="16" class="org-icon" />
                  {{ getOrganizationName(device.organizationId) }}
                </div>
              </td>
              <td>
                <div class="status-badge" :class="device.status">
                  <span class="status-dot" :class="device.status"></span>
                  {{ formatStatus(device.status) }}
                </div>
              </td>
              <td class="time-cell">{{ formatDate(device.lastHeartbeat || device.lastSeen, true) }}</td>
              <td>
                <div class="action-buttons">
                  <button class="button-icon" @click="viewDevice(device)" title="查看详情">
                    <FluentIcons name="view" :size="18" />
                  </button>
                  <button class="button-icon delete-btn" @click="showDeleteConfirm(device)" title="删除设备">
                    <FluentIcons name="delete" :size="18" />
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- 设备详情模态框 -->
    <Transition name="modal-overlay" appear>
      <div v-if="selectedDevice" class="modal-overlay" @click="closeDeviceModal">
        <Transition name="modal-content" appear>
          <div v-if="selectedDevice" class="modal-content" @click.stop>
            <div class="modal-header">
              <h3>设备详情</h3>
              <button class="close-btn" @click="closeDeviceModal">×</button>
            </div>
            <div class="modal-body">
              <div class="device-detail">
                <div class="detail-section">
                  <h4 class="section-title">基本信息</h4>
                  <div class="detail-row">
                    <strong>设备名称:</strong>
                    <span>{{ selectedDevice.name }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>设备 ID:</strong>
                    <span class="mono-font">{{ selectedDevice.id }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>IP 地址:</strong>
                    <span>{{ selectedDevice.ipAddress }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>MAC 地址:</strong>
                    <span class="mono-font">{{ selectedDevice.macAddress || 'N/A' }}</span>
                  </div>
                </div>

                <div class="detail-section">
                  <h4 class="section-title">系统信息</h4>
                  <div class="detail-row">
                    <strong>操作系统:</strong>
                    <span>{{ selectedDevice.osVersion || 'N/A' }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>软件版本:</strong>
                    <span>{{ selectedDevice.appVersion || '1.0.0' }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>.NET 版本:</strong>
                    <span>{{ selectedDevice.dotnetVersion || 'N/A' }}</span>
                  </div>
                </div>

                <div class="detail-section">
                  <h4 class="section-title">组织信息</h4>
                  <div class="detail-row">
                    <strong>所属组织:</strong>
                    <span>{{ getOrganizationName(selectedDevice.organizationId) }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>组织 ID:</strong>
                    <span class="mono-font">{{ selectedDevice.organizationId || 'N/A' }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>注册时间:</strong>
                    <span>{{ formatDate(selectedDevice.registeredAt, false) }}</span>
                  </div>
                </div>

                <div class="detail-section">
                  <h4 class="section-title">登记信息</h4>
                  <div class="detail-row">
                    <strong>联系电话:</strong>
                    <span>{{ selectedDevice.contactPhone || '未登记' }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>班级:</strong>
                    <span>{{ selectedDevice.className || '未登记' }}</span>
                  </div>
                  <div class="detail-row">
                    <strong>负责人:</strong>
                    <span>{{ selectedDevice.personInCharge || '未登记' }}</span>
                  </div>
                </div>

                <div class="detail-section">
                  <h4 class="section-title">运行状态</h4>
                  <div class="detail-row">
                    <strong>状态:</strong>
                    <span class="status-badge" :class="selectedDevice.status">
                      {{ formatStatus(selectedDevice.status) }}
                    </span>
                  </div>
                  <div class="detail-row">
                    <strong>最后在线:</strong>
                    <span>{{ formatDate(selectedDevice.lastHeartbeat || selectedDevice.lastSeen, true) }}</span>
                  </div>
                  <div class="detail-row" v-if="selectedDevice.exitTime">
                    <strong>退出时间:</strong>
                    <span>{{ formatDate(selectedDevice.exitTime, false) }}</span>
                  </div>
                  <div class="detail-row" v-if="selectedDevice.offlineReason">
                    <strong>{{ selectedDevice.status === 'logged_out' ? '退出原因' : '离线原因' }}:</strong>
                    <span>{{ formatOfflineReason(selectedDevice.offlineReason) }}</span>
                  </div>
                  <div class="detail-row" v-if="selectedDevice.cpuUsage !== undefined">
                    <strong>CPU 使用率:</strong>
                    <span class="metric-value">{{ selectedDevice.cpuUsage }}%</span>
                  </div>
                  <div class="detail-row" v-if="selectedDevice.memoryUsage !== undefined">
                    <strong>内存使用率:</strong>
                    <span class="metric-value">{{ selectedDevice.memoryUsage }}%</span>
                  </div>
                  <div class="detail-row" v-if="selectedDevice.diskUsage !== undefined">
                    <strong>磁盘使用率:</strong>
                    <span class="metric-value">{{ selectedDevice.diskUsage }}%</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </Transition>
      </div>
    </Transition>

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
import { ref, computed, onMounted, onUnmounted, inject } from 'vue'
import { useRoute } from 'vue-router'
import { deviceAPI, organizationAPI } from '@/api/organization'
import CustomDropdown from '@/components/CustomDropdown.vue'
import FluentIcons from '@/components/FluentIcons.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { notify } from '@/utils/notification'

const route = useRoute()
const devices = ref([])
const organizations = ref([])
const selectedDeviceId = ref(null)
const searchQuery = ref('')
const filterStatus = ref('')
const filterOrg = ref('')
const showConfirmDialog = ref(false)
const confirmMessage = ref('')
const currentDevice = ref(null)

const selectedDevice = computed(() => {
  if (!selectedDeviceId.value) return null
  return devices.value.find(device => device.id === selectedDeviceId.value) || null
})

const filteredDevices = computed(() => {
  let result = devices.value
  
  // 按组织过滤
  if (filterOrg.value) {
    result = result.filter(d => d.organizationId === filterOrg.value)
  }
  
  // 按状态过滤
  if (filterStatus.value) {
    result = result.filter(d => d.status === filterStatus.value)
  }
  
  // 按关键字搜索
  if (searchQuery.value) {
    const query = searchQuery.value.toLowerCase()
    result = result.filter(d => 
      d.name.toLowerCase().includes(query) || 
      d.ipAddress.toLowerCase().includes(query) ||
      (d.macAddress && d.macAddress.toLowerCase().includes(query))
    )
  }
  
  return result
})

const formatStatus = (status) => {
  const statusMap = {
    online: '在线',
    offline: '离线',
    logged_out: '已退出'
  }
  return statusMap[status] || '未知'
}

const formatOfflineReason = (reason) => {
  const reasonMap = {
    heartbeat_timeout: '心跳超时',
    network_error: '网络错误',
    user_logout: '用户主动退出',
    shutdown: '正常关机',
    unknown: '未知原因'
  }
  return reasonMap[reason] || reason
}

const statusOptions = computed(() => [
  { value: '', label: '所有状态', iconName: 'list' },
  { value: 'online', label: '在线', iconName: 'check_circle' },
  { value: 'offline', label: '离线', iconName: 'error' },
  { value: 'logged_out', label: '已退出', iconName: 'logout' }
])

const orgOptions = computed(() => [
  { value: '', label: '所有组织', iconName: 'building' },
  ...organizations.value.map(org => ({
    value: org.id,
    label: org.name,
    iconName: 'group'
  }))
])

const loadDevices = async () => {
  try {
    const data = await deviceAPI.getAll()
    devices.value = Array.isArray(data) ? data : []
  } catch (error) {
    console.error('Failed to load devices:', error)
    devices.value = []
  }
}

// 实时刷新设备状态
let refreshInterval = null

const startAutoRefresh = () => {
  if (refreshInterval) return
  // 每 2 秒刷新一次
  refreshInterval = setInterval(async () => {
    await loadDevices()
  }, 1000)
}

const stopAutoRefresh = () => {
  if (refreshInterval) {
    clearInterval(refreshInterval)
    refreshInterval = null
  }
}

const loadOrganizations = async () => {
  try {
    const data = await organizationAPI.getAll()
    organizations.value = Array.isArray(data) ? data : []
  } catch (error) {
    console.error('Failed to load organizations:', error)
    organizations.value = []
  }
}

const getOrganizationName = (orgId) => {
  const org = organizations.value.find(o => o.id === orgId)
  return org ? org.name : '未知组织'
}

const viewDevice = (device) => {
  selectedDeviceId.value = device.id
}

const closeDeviceModal = () => {
  selectedDeviceId.value = null
}

const showDeleteConfirm = (device) => {
  currentDevice.value = device
  confirmMessage.value = `确定要删除设备 "${device.name}" 吗？`
  showConfirmDialog.value = true
}

const confirmDelete = async () => {
  if (currentDevice.value) {
    try {
      await deviceAPI.delete(currentDevice.value.id)
      await loadDevices()
      // 如果删除的是当前查看的设备，关闭模态框
      if (selectedDeviceId.value === currentDevice.value.id) {
        selectedDeviceId.value = null
      }
      notify.success('设备删除成功！')
    } catch (error) {
      console.error('删除设备失败:', error)
      notify.error('删除设备失败，请重试')
    } finally {
      showConfirmDialog.value = false
      currentDevice.value = null
    }
  }
}

const cancelDelete = () => {
  showConfirmDialog.value = false
  currentDevice.value = null
}

const formatDate = (date, showSeconds = false) => {
  if (!date) return '从未在线'
  const d = new Date(date)
  const now = new Date()
  const diff = now - d
  
  // 小于 1 分钟
  if (diff < 60000) {
    return showSeconds ? `${Math.floor(diff / 1000)}秒前` : '刚刚'
  }
  // 小于 1 小时
  if (diff < 3600000) {
    return `${Math.floor(diff / 60000)}分钟前`
  }
  // 小于 24 小时
  if (diff < 86400000) {
    return `${Math.floor(diff / 3600000)}小时前`
  }
  // 超过 24 小时，显示具体日期
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')} ${String(d.getHours()).padStart(2, '0')}:${String(d.getMinutes()).padStart(2, '0')}`
}

onMounted(async () => {
  if (route?.query?.org) {
    filterOrg.value = parseInt(route.query.org)
  }
  await Promise.all([loadDevices(), loadOrganizations()])
  startAutoRefresh()
})

onUnmounted(() => {
  stopAutoRefresh()
})
</script>

<style scoped>
.devices-page {
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

.input-field {
  height: 36px;
  padding: 0 14px;
  background: rgba(255, 255, 255, 0.04);
  border: 1px solid rgba(0, 0, 0, 0.5);
  border-radius: 8px;
  color: var(--fui-text);
  font-size: 1em;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  outline: none;
}

.input-field:hover {
  background: rgba(0, 0, 0, 0.06);
  border-color: rgba(255, 255, 255, 0.12);
}

.input-field:focus {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(0, 0, 0, 0.2);
  box-shadow: 0 0 0 3px rgba(255, 255, 255, 0.06);
}

.input-field::placeholder {
  color: var(--fui-text-secondary);
  opacity: 0.6;
}

select.input-field {
  appearance: none;
  background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='%23888' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'%3E%3C/polyline%3E%3C/svg%3E");
  background-repeat: no-repeat;
  background-position: right 12px center;
  padding-right: 36px;
  cursor: pointer;
  border-radius: 8px !important;
}

.devices-list {
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

.devices-table {
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
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  opacity: 0;
  animation: rowFadeIn 0.4s cubic-bezier(0.34, 1.56, 0.64, 1) both;
  border-radius: 4px;
}

tbody tr:nth-child(even) {
  background: rgba(255, 255, 255, 0.02); /* 斑马纹效果 */
}

/* 最后一行圆角 */
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
    transform: translateY(10px) scale(0.98);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}

tbody tr:hover {
  background-color: rgba(255, 255, 255, 0.08);
  border-radius: 4px;
  transform: translateX(4px) scale(1.01);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.device-name {
  display: flex;
  align-items: center;
  gap: 10px;
  font-weight: 500;
}

.device-icon {
  opacity: 0.7;
  flex-shrink: 0;
}

.org-name {
  display: flex;
  align-items: center;
  gap: 8px;
}

.org-icon {
  opacity: 0.6;
  flex-shrink: 0;
}

.status-badge {
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

.status-dot {
  width: 6px;
  height: 6px;
  border-radius: 50%;
  flex-shrink: 0;
  position: relative;
}

.status-dot.online {
  background: linear-gradient(135deg, #10b981 0%, #059669 100%);
  box-shadow: 0 0 0 2px rgba(16, 185, 129, 0.15), 0 0 8px rgba(16, 185, 129, 0.4);
}

.status-dot.offline {
  background: linear-gradient(135deg, #ef4444 0%, #dc2626 100%);
  box-shadow: 0 0 0 2px rgba(239, 68, 68, 0.15), 0 0 8px rgba(239, 68, 68, 0.4);
}

.status-dot.logged_out {
  background: linear-gradient(135deg, #9ca3af 0%, #6b7280 100%);
  box-shadow: 0 0 0 2px rgba(156, 163, 175, 0.15), 0 0 8px rgba(156, 163, 175, 0.3);
}

.status-badge.online {
  background: linear-gradient(135deg, rgba(16, 185, 129, 0.12) 0%, rgba(5, 150, 105, 0.08) 100%);
  color: #10b981;
  border: 1px solid rgba(16, 185, 129, 0.2);
}

.status-badge.offline {
  background: linear-gradient(135deg, rgba(239, 68, 68, 0.12) 0%, rgba(220, 38, 38, 0.08) 100%);
  color: #ef4444;
  border: 1px solid rgba(239, 68, 68, 0.2);
}

.status-badge.logged_out {
  background: linear-gradient(135deg, rgba(156, 163, 175, 0.12) 0%, rgba(107, 114, 128, 0.08) 100%);
  color: #6b7280;
  border: 1px solid rgba(156, 163, 175, 0.2);
}

.time-cell {
  color: var(--fui-text-secondary);
  font-size: 0.8125em;
}

.action-buttons {
  display: flex;
  gap: 8px;
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
}

.button-icon:hover {
  background: rgba(255, 255, 255, 0.08);
  border-color: rgba(255, 255, 255, 0.15);
  transform: scale(1.05);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

.button-icon.delete-btn {
  color: #ef4444;
  border-color: rgba(239, 68, 68, 0.2);
}

.button-icon.delete-btn:hover {
  background: rgba(239, 68, 68, 0.1);
  border-color: rgba(239, 68, 68, 0.3);
  box-shadow: 0 2px 8px rgba(239, 68, 68, 0.2);
}

.button-icon:active {
  transform: scale(0.98);
}

.button-icon svg {
  width: 18px;
  height: 18px;
}

/* 模态框样式 */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-overlay-enter-active,
.modal-overlay-leave-active {
  transition: all 0.4s cubic-bezier(0.4, 0, 0.2, 1);
}

.modal-overlay-enter-from,
.modal-overlay-leave-to {
  opacity: 0;
}

.modal-content {
  background: var(--fui-background);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: 12px;
  max-width: 800px;
  width: 90%;
  max-height: 85vh;
  overflow-y: auto;
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
}

.modal-content-enter-active {
  transition: all 0.4s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.modal-content-leave-active {
  transition: all 0.3s cubic-bezier(0.4, 0, 1, 1);
}

.modal-content-enter-from {
  opacity: 0;
  transform: translateY(-30px) scale(0.9);
}

.modal-content-leave-to {
  opacity: 0;
  transform: translateY(20px) scale(0.95);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 20px 24px;
  border-bottom: 1px solid rgba(255, 255, 255, 0.08);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.125em;
  font-weight: 600;
  color: var(--fui-text);
}

.close-btn {
  width: 32px;
  height: 32px;
  padding: 0;
  background: transparent;
  border: none;
  border-radius: 6px;
  color: var(--fui-text-secondary);
  cursor: pointer;
  font-size: 1.25em;
  display: flex;
  align-items: center;
  justify-content: center;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.close-btn:hover {
  background: rgba(255, 255, 255, 0.1);
  color: var(--fui-text);
  transform: rotate(90deg);
}

[data-theme='dark'] .close-btn:hover {
  background: rgba(255, 255, 255, 0.15);
}

.close-btn:active {
  transform: rotate(180deg) scale(0.9);
}

.modal-body {
  padding: 24px;
}

.device-detail {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.detail-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.section-title {
  margin: 0 0 4px 0;
  font-size: 0.8125em;
  font-weight: 600;
  color: var(--fui-text-secondary);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.detail-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.05);
}

.detail-row:last-child {
  border-bottom: none;
}

.detail-row strong {
  color: var(--fui-text-secondary);
  font-weight: 500;
  font-size: 0.875em;
}

.detail-row span {
  color: var(--fui-text);
  font-size: 0.875em;
}

.mono-font {
  font-family: 'Consolas', 'Monaco', monospace;
  font-size: 0.8125em;
}

.metric-value {
  font-weight: 600;
  color: var(--fui-text);
}

/* 移动端响应式 */
@media (max-width: 1024px) {
  .devices-page {
    padding: 16px;
    gap: 16px;
  }

  .page-header {
    flex-direction: column;
    gap: 16px;
    align-items: flex-start;
  }

  .header-actions {
    flex-direction: column;
    width: 100%;
    gap: 10px;
  }

  .search-box {
    width: 100%;
  }

  .search-input {
    width: 100%;
  }

  .filter-group {
    flex-direction: column;
    width: 100%;
  }

  .filter-group :deep(.custom-dropdown) {
    width: 100%;
    min-width: auto;
  }

  .devices-list {
    overflow-x: auto;
  }

  table {
    font-size: 0.8125em;
  }

  thead th,
  tbody td {
    padding: 10px 12px;
  }
}

@media (max-width: 768px) {
  .devices-page {
    padding: 12px;
  }

  .page-title h2 {
    font-size: 1.125em;
  }

  .devices-table {
    overflow-x: scroll;
    -webkit-overflow-scrolling: touch;
  }

  .status-badge {
    padding: 4px 8px;
    font-size: 0.6875em;
  }

  .status-dot {
    width: 5px;
    height: 5px;
  }

  .button-icon {
    width: 30px;
    height: 30px;
  }

  .button-icon svg {
    width: 16px;
    height: 16px;
  }

  .modal-content {
    width: 95%;
    max-height: 90vh;
  }

  .modal-header {
    padding: 16px 20px;
  }

  .modal-body {
    padding: 20px;
  }

  .detail-row {
    flex-direction: column;
    align-items: flex-start;
    gap: 4px;
  }

  .detail-row strong {
    font-size: 0.8125em;
  }

  .detail-row span {
    font-size: 0.8125em;
    text-align: left;
    max-width: 100%;
    word-break: break-word;
  }
}

@media (max-width: 480px) {
  .devices-page {
    padding: 8px;
  }

  .page-title h2 {
    font-size: 1em;
  }

  .input-field {
    font-size: 0.8125em;
  }

  thead th {
    font-size: 0.6875em;
    padding: 8px 10px;
  }

  tbody td {
    font-size: 0.75em;
    padding: 8px 10px;
  }

  .device-name {
    font-size: 0.8125em;
  }

  .th-icon {
    width: 14px;
    height: 14px;
  }
}

</style>
