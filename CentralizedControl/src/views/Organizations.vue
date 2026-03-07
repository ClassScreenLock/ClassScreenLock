<template>
  <div class="organizations-page">
    <div class="page-header">
      <h2>组织管理</h2>
      <button class="button-primary" @click="openAddModal">添加组织</button>
    </div>

    <div class="organizations-list card">
      <div v-if="organizations.length === 0" class="empty-state">
        暂无组织数据，请点击上方按钮添加组织
      </div>
      <div v-else class="organization-grid">
        <div v-for="org in organizations" :key="org.id" class="organization-card">
          <div class="org-header">
            <h3>{{ org.name }}</h3>
            <div class="org-actions">
              <button class="button-primary" @click="editOrganization(org)">编辑</button>
              <button class="button-primary" @click="viewDevices(org)">查看设备</button>
            </div>
          </div>
          <div class="org-details">
            <p><strong>组织 ID:</strong> <code class="org-id">{{ org.id }}</code></p>
            <p><strong>描述:</strong> {{ org.description || '暂无描述' }}</p>
            <p><strong>所在地区:</strong> {{ getFullRegion(org) || '未知' }}</p>
            <p><strong>创建时间:</strong> {{ formatDate(org.createdAt) }}</p>
            <p><strong>设备数量:</strong> {{ org.deviceCount || 0 }}</p>
          </div>
          <div class="org-footer">
            <button class="button-primary" @click="showDeleteConfirm(org)">删除</button>
          </div>
        </div>
      </div>
    </div>

    <!-- 添加/编辑组织模态框 -->
    <Transition name="modal-overlay" appear>
      <div v-if="showModal" class="modal-overlay" @click="closeModal">
        <Transition name="modal-content" appear>
          <div v-if="showModal" class="modal-content" @click.stop>
            <div class="modal-header">
              <h3>{{ modalTitle }}</h3>
              <button class="close-btn" @click="closeModal">×</button>
            </div>
            <div class="modal-body">
              <form @submit.prevent="saveOrganization">
                <div class="form-group">
                  <label>组织名称 *</label>
                  <input 
                    :key="'org-name-' + modalTitle"
                    type="text" 
                    v-model="currentOrg.name" 
                    class="input-field" 
                    placeholder="请输入组织名称"
                    required
                    autocomplete="off"
                  >
                </div>

                <div class="form-group" v-if="modalTitle === '添加组织'">
                  <label>地区选择</label>
                  <div class="region-selectors">
                    <CustomDropdown 
                      v-model="currentOrg.provinceCode" 
                      :options="provinces.map(p => ({ value: p.code, label: p.name }))" 
                      placeholder="请选择省份"
                      @update:modelValue="updateCities"
                    />
                    <CustomDropdown 
                      v-model="currentOrg.cityCode" 
                      :options="(regionData.cities[currentOrg.provinceCode] || []).map(c => ({ value: c.code, label: c.name }))" 
                      placeholder="请选择城市"
                      @update:modelValue="updateDistricts"
                    />
                    <CustomDropdown 
                      v-model="currentOrg.districtCode" 
                      :options="(regionData.districts[currentOrg.cityCode] || []).map(d => ({ value: d.code, label: d.name }))" 
                      placeholder="请选择区县"
                    />
                  </div>
                  <small class="form-hint">提示：地区选择后不可更改</small>
                </div>
                <div class="form-group" v-else>
                  <label>所在地区</label>
                  <div class="region-info">
                    {{ getFullRegion(currentOrg) || '未知' }}
                  </div>
                  <small class="form-hint">地区一旦选择不可更改</small>
                </div>
                <div class="form-group">
                  <label>描述</label>
                  <textarea 
                    v-model="currentOrg.description" 
                    class="input-field" 
                    placeholder="请输入组织描述"
                    rows="3"
                  ></textarea>
                </div>
                <div class="form-actions">
                  <button type="button" class="button-primary" @click="closeModal">取消</button>
                  <button type="submit" class="button-primary">保存</button>
                </div>
              </form>
            </div>
          </div>
        </Transition>
      </div>
    </Transition>
    </div>

    <!-- 确认删除对话框 -->
    <ConfirmDialog
      v-model:visible="showConfirmDialog"
      :title="'确认删除'"
      :message="confirmMessage"
      @confirm="confirmDelete"
      @cancel="cancelDelete"
    />
</template>

<script setup>
import { ref, onMounted, computed, inject } from 'vue'
import { useRouter } from 'vue-router'
import { organizationAPI } from '@/api/organization'
import regionData from '@/data/regionData'
import CustomDropdown from '@/components/CustomDropdown.vue'
import ConfirmDialog from '@/components/ConfirmDialog.vue'
import { notify } from '@/utils/notification'

// 调试信息
console.log('Region data imported:', regionData)
console.log('Provinces length:', regionData.provinces.length)
console.log('Cities object keys count:', Object.keys(regionData.cities).length)
console.log('Cities sample:', regionData.cities['110000'])

const router = useRouter()
const organizations = ref([])
const showModal = ref(false)
const modalTitle = ref('')
const currentOrg = ref({
  name: '',
  description: '',
  provinceCode: '',
  cityCode: '',
  districtCode: ''
})
const showConfirmDialog = ref(false)
const confirmMessage = ref('')
const currentDeleteOrg = ref(null)

const provinces = ref(regionData.provinces)
const cities = ref([])
const districts = ref([])

// 使用注入的通知方法
const showNotification = inject('showNotification')

// 调试信息
console.log('Initial provinces:', provinces.value)
console.log('Region data cities:', regionData.cities)

const updateCities = () => {
  const provinceCode = currentOrg.value.provinceCode
  console.log('Province code selected:', provinceCode)
  console.log('Cities object keys:', Object.keys(regionData.cities))
  console.log('Cities for province:', regionData.cities[provinceCode])
  // 确保省份代码存在于cities对象中
  if (regionData.cities[provinceCode]) {
    cities.value = regionData.cities[provinceCode]
    console.log('Updated cities:', cities.value)
  } else {
    cities.value = []
    console.log('No cities found for province:', provinceCode)
  }
  currentOrg.value.cityCode = ''
  currentOrg.value.districtCode = ''
  updateDistricts()
}

const updateDistricts = () => {
  const cityCode = currentOrg.value.cityCode
  console.log('City code selected:', cityCode)
  console.log('Districts for city:', regionData.districts[cityCode])
  districts.value = regionData.districts[cityCode] || []
  console.log('Updated districts:', districts.value)
  currentOrg.value.districtCode = ''
}

const loadOrganizations = async () => {
  try {
    const data = await organizationAPI.getAll()
    const orgs = Array.isArray(data) ? data : []
    
    // 加载每个组织的设备数量
    for (const org of orgs) {
      try {
        const devices = await organizationAPI.getDevices(org.id)
        org.deviceCount = Array.isArray(devices) ? devices.length : 0
      } catch (error) {
        console.error(`Failed to load devices for org ${org.id}:`, error)
        org.deviceCount = 0
      }
    }
    
    organizations.value = orgs
  } catch (error) {
    console.error('Failed to load organizations:', error)
    organizations.value = []
  }
}

const openAddModal = () => {
  currentOrg.value = { 
    name: '', 
    description: '', 
    provinceCode: '',
    cityCode: '',
    districtCode: ''
  }
  cities.value = []
  districts.value = []
  modalTitle.value = '添加组织'
  showModal.value = true
}

const editOrganization = (org) => {
  currentOrg.value = { 
    ...org
  }
  cities.value = []
  districts.value = []
  modalTitle.value = '编辑组织'
  showModal.value = true
}

const closeModal = () => {
  showModal.value = false
}

const saveOrganization = async () => {
  try {
    if (modalTitle.value === '编辑组织') {
      // 更新组织
      await organizationAPI.update(currentOrg.value.id, currentOrg.value)
    } else {
      // 创建组织，发送完整的地区信息
      await organizationAPI.create({
        name: currentOrg.value.name,
        description: currentOrg.value.description,
        provinceCode: currentOrg.value.provinceCode,
        cityCode: currentOrg.value.cityCode,
        districtCode: currentOrg.value.districtCode
      })
    }
    await loadOrganizations()
    closeModal()
  } catch (error) {
    console.error('Failed to save organization:', error)
    if (showNotification) {
      showNotification('保存失败，请重试', 'error')
    } else {
      notify.error('保存失败，请重试')
    }
  }
}

const showDeleteConfirm = (org) => {
  currentDeleteOrg.value = org
  confirmMessage.value = `确定要删除组织 "${org.name}" 吗？`
  showConfirmDialog.value = true
}

const confirmDelete = async () => {
  if (currentDeleteOrg.value) {
    try {
      await organizationAPI.delete(currentDeleteOrg.value.id)
      await loadOrganizations()
      if (showNotification) {
        showNotification('组织删除成功', 'success')
      } else {
        notify.success('组织删除成功')
      }
    } catch (error) {
      console.error('Failed to delete organization:', error)
      if (showNotification) {
        showNotification('删除失败，请重试', 'error')
      } else {
        notify.error('删除失败，请重试')
      }
    } finally {
      showConfirmDialog.value = false
      currentDeleteOrg.value = null
    }
  }
}

const cancelDelete = () => {
  showConfirmDialog.value = false
  currentDeleteOrg.value = null
}

const viewDevices = (org) => {
  router.push(`/devices?org=${org.id}`)
}

const formatDate = (date) => {
  if (!date) return ''
  const d = new Date(date)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const getFullRegion = (org) => {
  if (!org) return ''
  
  const regions = []
  if (org.provinceName) regions.push(org.provinceName)
  if (org.cityName) regions.push(org.cityName)
  if (org.districtName) regions.push(org.districtName)
  
  return regions.join(' ')
}

onMounted(() => {
  loadOrganizations()
})
</script>

<style scoped>
.organizations-page {
  display: flex;
  flex-direction: column;
  gap: 24px;
  animation: pageFadeIn 0.8s cubic-bezier(0.4, 0, 0.2, 1);
}

@keyframes pageFadeIn {
  from {
    opacity: 0;
    transform: translateY(20px);
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
}

.page-header h2 {
  margin: 0;
  color: var(--fui-text);
}

.organizations-list {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: var(--fui-radius);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
  padding: 24px;
  animation: containerFadeIn 0.6s cubic-bezier(0.4, 0, 0.2, 1);
  animation-delay: 0.2s;
  animation-fill-mode: both;
  font-size: var(--fui-font-size-base);
}

[data-theme='dark'] .organizations-list {
  background: rgba(30, 30, 30, 0.6);
  border: 1px solid rgba(255, 255, 255, 0.1);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.3);
}

@keyframes containerFadeIn {
  from {
    opacity: 0;
    transform: translateY(20px);
  }
  to {
    opacity: 1;
    transform: translateY(0);
  }
}

.empty-state {
  text-align: center;
  padding: 40px 20px;
  color: var(--fui-text-secondary);
  font-size: 1em;
}

.organization-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(350px, 1fr));
  gap: 20px;
}

.organization-card {
  background: rgba(255, 255, 255, 0.05);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: var(--fui-radius);
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  transition: all 0.4s cubic-bezier(0.4, 0, 0.2, 1);
  opacity: 0;
  transform: translateY(20px) scale(0.95);
  animation: cardFadeIn 0.6s cubic-bezier(0.34, 1.56, 0.64, 1);
  animation-fill-mode: both;
}

[data-theme='dark'] .organization-card {
  background: rgba(45, 45, 45, 0.5);
  border: 1px solid rgba(255, 255, 255, 0.08);
}

.organization-card:nth-child(1) { animation-delay: 0.3s; }
.organization-card:nth-child(2) { animation-delay: 0.4s; }
.organization-card:nth-child(3) { animation-delay: 0.5s; }
.organization-card:nth-child(4) { animation-delay: 0.6s; }
.organization-card:nth-child(5) { animation-delay: 0.7s; }
.organization-card:nth-child(6) { animation-delay: 0.8s; }

@keyframes cardFadeIn {
  from {
    opacity: 0;
    transform: translateY(30px) scale(0.9);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}

.org-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.org-header h3 {
  margin: 0;
  color: var(--fui-text);
  font-size: 1.125em;
}

.org-actions {
  display: flex;
  gap: 8px;
}

.organization-card:hover {
  transform: translateY(-6px) scale(1.02);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.2);
  background: rgba(255, 255, 255, 0.1);
  border-color: rgba(255, 255, 255, 0.3);
}

[data-theme='dark'] .organization-card:hover {
  background: rgba(55, 55, 55, 0.6);
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.5);
  border-color: rgba(255, 255, 255, 0.15);
}

.org-details p {
  margin: 4px 0;
  font-size: 0.875em;
  color: var(--fui-text);
}

.org-details .org-id {
  font-family: 'Courier New', monospace;
  background: var(--fui-background);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.8125em;
}

[data-theme='dark'] .org-details .org-id {
  background: rgba(60, 60, 60, 0.8);
  color: #e0e0e0;
}

.org-footer {
  margin-top: auto;
  display: flex;
  justify-content: flex-end;
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
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
  background: var(--fui-surface);
  border-radius: var(--fui-radius);
  box-shadow: 0 10px 30px rgba(0, 0, 0, 0.2);
  width: 90%;
  max-width: 700px;
  max-height: 90vh;
  overflow-y: auto;
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

[data-theme='dark'] .modal-content {
  background: rgba(45, 45, 45, 0.95);
  border: 1px solid rgba(255, 255, 255, 0.1);
  box-shadow: 0 10px 40px rgba(0, 0, 0, 0.5);
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
  color: var(--fui-text);
}

.close-btn {
  background: none;
  border: none;
  font-size: 1.5em;
  cursor: pointer;
  color: var(--fui-text-secondary);
  padding: 0;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 50%;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.close-btn:hover {
  color: var(--fui-text);
  background: rgba(0, 0, 0, 0.05);
  transform: rotate(90deg);
}

[data-theme='dark'] .close-btn:hover {
  background: rgba(255, 255, 255, 0.1);
}

.close-btn:active {
  transform: rotate(180deg) scale(0.9);
}

.modal-body {
  padding: 24px;
}

.form-group {
  margin-bottom: 16px;
}

.form-group label {
  display: block;
  margin-bottom: 6px;
  font-weight: 500;
  color: #333;
}

[data-theme='dark'] .form-group label {
  color: #e0e0e0;
}

.form-group .region-info {
  padding: 10px 12px;
  background-color: #f5f5f5;
  border: 1px solid #e0e0e0;
  border-radius: 4px;
  font-size: 0.875em;
  color: #333;
}

[data-theme='dark'] .form-group .region-info {
  background-color: rgba(60, 60, 60, 0.8);
  border-color: rgba(255, 255, 255, 0.1);
  color: #e0e0e0;
}

.form-hint {
  display: block;
  margin-top: 4px;
  font-size: 0.75em;
  color: #666;
}

[data-theme='dark'] .form-hint {
  color: #999;
}

.form-actions {
  display: flex;
  gap: 12px;
  justify-content: flex-end;
  margin-top: 24px;
}

textarea.input-field {
  resize: vertical;
  min-height: 80px;
}

[data-theme='dark'] .input-field {
  background-color: rgba(60, 60, 60, 0.8);
  border-color: rgba(255, 255, 255, 0.1);
  color: #e0e0e0;
}

[data-theme='dark'] .input-field:focus {
  background-color: rgba(70, 70, 70, 0.9);
}

[data-theme='dark'] .btn-primary {
  background-color: #0078d4;
}

[data-theme='dark'] .btn-primary:hover {
  background-color: #1084d8;
}

[data-theme='dark'] .btn-secondary {
  background-color: rgba(255, 255, 255, 0.1);
  color: #e0e0e0;
}

[data-theme='dark'] .btn-secondary:hover {
  background-color: rgba(255, 255, 255, 0.15);
}

.region-selectors {
  display: flex;
  gap: 12px;
  margin-bottom: 8px;
}

@media (max-width: 768px) {
  .region-selectors {
    flex-direction: column;
  }
}
</style>
