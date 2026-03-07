<template>
  <div class="network-intercept-page">
    <div class="page-header">
      <h2>网络拦截配置</h2>
      <div class="header-actions">
        <CustomDropdown
          v-model="selectedOrg"
          :options="orgOptions"
          placeholder="选择组织"
          style="width: 250px;"
        />
        <button class="button-primary" @click="saveConfig" :disabled="!selectedOrg">保存配置</button>
      </div>
    </div>

    <div class="config-container card">
      <div v-if="!selectedOrg" class="empty-state">
        请先选择要配置的组织
      </div>
      <div v-else class="config-content">
        <div class="config-section">
          <h3>全局设置</h3>
          <div class="form-grid">
            <div class="form-group">
              <label>
                启用网络拦截
                <CustomSwitch v-model="config.enabled" />
              </label>
            </div>
          </div>
          <div class="tip-box">
            <strong>💡 提示：</strong> 如果拦截不生效，请尝试：
            <ul>
              <li>点击"保存配置"并允许管理员权限</li>
              <li>关闭浏览器的"安全 DNS"(DNS-over-HTTPS) 设置</li>
              <li>重启浏览器或清理浏览器缓存</li>
            </ul>
          </div>
        </div>

        <div class="config-section">
          <h3>域名拦截规则</h3>
          <div class="rules-editor">
            <div class="rules-header">
              <span>拦截域名列表</span>
              <button class="button-primary" @click="addDomainRule">添加域名</button>
            </div>
            <div class="rules-list">
              <div v-for="(rule, index) in config.domainRules" :key="index" class="rule-item">
                <div class="rule-content">
                  <div class="form-grid">
                    <div class="form-group">
                      <label>域名</label>
                      <input 
                        type="text" 
                        v-model="rule.domain" 
                        class="input-field" 
                        placeholder="例如：bilibili.com"
                      >
                    </div>
                    <div class="form-group">
                      <label>描述</label>
                      <input 
                        type="text" 
                        v-model="rule.description" 
                        class="input-field" 
                        placeholder="描述信息"
                      >
                    </div>
                  </div>
                  <div class="form-group">
                    <label>
                      启用此规则
                      <CustomSwitch v-model="rule.isEnabled" />
                    </label>
                  </div>
                </div>
                <button class="remove-btn" @click="removeDomainRule(index)">删除</button>
              </div>
              <div v-if="config.domainRules.length === 0" class="empty-rules">
                暂无规则，请点击上方按钮添加
              </div>
            </div>
          </div>
        </div>




      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch, onMounted, computed, inject } from 'vue'
import { organizationAPI } from '@/api/organization'
import CustomDropdown from '@/components/CustomDropdown.vue'
import CustomSwitch from '@/components/CustomSwitch.vue'
import { notify } from '@/utils/notification'

// 下拉框选项数据
const orgOptions = computed(() => {
  return organizations.value.map(org => ({
    value: org.id,
    label: org.name,
    iconName: 'building'
  }))
})





const organizations = ref([])
const selectedOrg = ref('')

const defaultConfig = {
  enabled: true,
  mode: 'blacklist',
  domainRules: []
}

const config = ref(JSON.parse(JSON.stringify(defaultConfig)))

const loadOrganizations = async () => {
  try {
    const data = await organizationAPI.getAll()
    organizations.value = Array.isArray(data) ? data : []
  } catch (error) {
    console.error('Failed to load organizations:', error)
    organizations.value = []
  }
}

const loadConfig = async () => {
  if (!selectedOrg.value) return
  
  try {
    const data = await organizationAPI.getNetworkConfig(selectedOrg.value)
    if (data) {
      config.value = { ...defaultConfig, ...data }
    } else {
      config.value = JSON.parse(JSON.stringify(defaultConfig))
    }
  } catch (error) {
    console.error('Failed to load config:', error)
    config.value = JSON.parse(JSON.stringify(defaultConfig))
  }
}

const addDomainRule = () => {
  config.value.domainRules.push({
    domain: '',
    description: '',
    isEnabled: true
  })
}

const removeDomainRule = (index) => {
  config.value.domainRules.splice(index, 1)
}



const saveConfig = async () => {
  if (!selectedOrg.value) return
  
  try {
    await organizationAPI.updateNetworkConfig(selectedOrg.value, config.value)
    notify.success('网络拦截配置保存成功！')
  } catch (error) {
    console.error('Failed to save config:', error)
    notify.error('保存失败，请重试')
  }
}

watch(selectedOrg, () => {
  loadConfig()
})

onMounted(() => {
  loadOrganizations()
})
</script>

<style scoped>
.network-intercept {
  display: flex;
  flex-direction: column;
  gap: 24px;
  animation: pageFadeIn 0.8s cubic-bezier(0.4, 0, 0.2, 1);
  font-size: var(--fui-font-size-base);
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

.header-actions {
  display: flex;
  gap: 12px;
  align-items: center;
}

.config-section {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: var(--fui-radius);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
  padding: 24px;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  opacity: 0;
  transform: translateY(20px);
  animation: sectionFadeIn 0.6s cubic-bezier(0.4, 0, 0.2, 1);
  animation-fill-mode: both;
}

.config-section:nth-child(1) { animation-delay: 0.2s; }
.config-section:nth-child(2) { animation-delay: 0.3s; }
.config-section:nth-child(3) { animation-delay: 0.4s; }

@keyframes sectionFadeIn {
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
  padding: 60px 20px;
  color: var(--fui-text-secondary);
  font-size: 16px;
}

.config-content {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.config-section {
  padding: 20px;
  border: 1px solid var(--fui-border);
  border-radius: var(--fui-radius);
}

.config-section h3 {
  margin: 0 0 16px 0;
  color: var(--fui-primary);
  font-size: 16px;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.form-group {
  margin-bottom: 16px;
}

.form-group label {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 6px;
  font-weight: 500;
  color: var(--fui-text);
  font-size: 14px;
}

.form-group input[type="checkbox"] {
  margin-right: 8px;
}

.form-group .custom-switch {
  margin-left: 12px;
}

.rules-editor {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.rules-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.rules-header span {
  font-weight: 500;
  color: var(--fui-text);
}

.rules-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.rule-item {
  display: flex;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--fui-border);
  border-radius: var(--fui-radius);
  align-items: flex-start;
}

.rule-content {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.rule-options {
  display: flex;
  gap: 16px;
}

.rule-options label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  margin: 0;
}

.remove-btn {
  background: #a80000;
  color: white;
  border: none;
  padding: 8px 16px;
  border-radius: var(--fui-radius);
  cursor: pointer;
  font-size: 13px;
  transition: background 0.2s;
}

.remove-btn:hover {
  background: #c50f1f;
}

.empty-rules {
  text-align: center;
  padding: 20px;
  color: var(--fui-text-secondary);
  font-size: 14px;
  border: 1px dashed var(--fui-border);
  border-radius: var(--fui-radius);
}

.tip-box {
  margin-top: 16px;
  padding: 16px;
  background: rgba(255, 255, 255, 0.05);
  border: 1px solid rgba(255, 255, 255, 0.1);
  border-radius: var(--fui-radius);
  font-size: 13px;
  color: var(--fui-text);
}

.tip-box ul {
  margin: 8px 0 0 0;
  padding-left: 20px;
}

.tip-box li {
  margin: 4px 0;
  color: var(--fui-text-secondary);
}

button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
