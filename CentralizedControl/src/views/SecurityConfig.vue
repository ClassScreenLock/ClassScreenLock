<template>
  <div class="security-config-page">
    <div class="page-header">
      <h2>安全中心配置</h2>
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

    <div class="config-container">
      <div v-if="!selectedOrg" class="empty-state">
        请先选择要配置的组织
      </div>
      <div v-else class="config-editor">
        <div class="editor-tabs">
          <button 
            class="tab-button" 
            :class="{ active: activeTab === 'visual' }"
            @click="activeTab = 'visual'"
          >
            可视化配置
          </button>
          <button 
            class="tab-button" 
            :class="{ active: activeTab === 'json' }"
            @click="activeTab = 'json'"
          >
            JSON 编辑
          </button>
        </div>

        <div v-if="activeTab === 'visual'" class="visual-editor">


          <div class="config-section">
            <h3>账户管理</h3>
            <div class="form-group">
              <button class="button-primary" @click="addAccount" :disabled="config.accounts && config.accounts.length >= 5">
                添加账户
              </button>
              <small>最多可添加 5 个账户</small>
            </div>
            
            <div v-if="config.accounts && config.accounts.length > 0" class="accounts-list">
              <div v-for="(account, index) in config.accounts" :key="index" class="account-item">
                <div class="account-fields">
                  <div class="form-group">
                    <label>用户名</label>
                    <input 
                      type="text" 
                      v-model="account.username" 
                      class="input-field"
                      placeholder="输入用户名"
                    >
                  </div>
                  <div class="form-group">
                    <label>密码</label>
                    <div class="password-input-container">
                      <input 
                        :type="showPassword ? 'text' : 'password'" 
                        v-model="account.password" 
                        class="input-field password-input"
                        placeholder="输入密码（留空则不修改）"
                      >
                      <button 
                        type="button" 
                        class="password-toggle"
                        @click="showPassword = !showPassword"
                      >
                        <FluentIcons :name="showPassword ? 'eye' : 'eye_off'" :size="18" />
                      </button>
                    </div>
                  </div>
                  <div class="form-group">
                    <label>账户类型</label>
                    <CustomDropdown
                      v-model="account.accountType"
                      :options="accountTypeOptions"
                      placeholder="选择账户类型"
                    />
                  </div>
                  <div class="form-group">
                    <label>账户 ID</label>
                    <input 
                      type="text" 
                      v-model="account.id" 
                      class="input-field"
                      placeholder="输入账户 ID（可选）"
                    >
                  </div>
                  <div class="form-group">
                    <label>
                      <input type="checkbox" v-model="account.isTwoFactorEnabled">
                      启用双重验证
                    </label>
                  </div>
                </div>
                <div class="form-actions">
                  <button class="button-primary" @click="showAccountTwoFactorConfig(index)" v-if="account.isTwoFactorEnabled && account.twoFactorSecret">
                    配置 2FA
                  </button>
                  <button class="button-danger" @click="removeAccount(index)">
                    删除
                  </button>
                </div>
              </div>
            </div>
            <div v-else class="empty-accounts">
              暂无账户配置
            </div>
          </div>
              


          <div class="config-section">
            <h3>安全配置</h3>
            <div class="form-grid">
              <div class="form-group">
                <label>
                  <input type="checkbox" v-model="config.security.isTwoFactorEnabled">
                  启用双重验证 (2FA)
                </label>
                <small>全局启用 2FA 后，各账户可以单独配置 2FA</small>
              </div>
              <div class="form-group">
                <label>登录验证模式</label>
                <CustomDropdown
                  v-model="config.security.loginVerificationMode"
                  :options="loginVerificationModeOptions"
                  placeholder="选择验证模式"
                  :disabled="!config.security.isTwoFactorEnabled"
                />
                <small v-if="!config.security.isTwoFactorEnabled">启用 2FA 后才能更改验证模式</small>
                <small v-else>选择登录时需要的验证方式</small>
              </div>
            </div>
          </div>

          <div class="config-section">
            <h3>权限设置</h3>
            <div class="form-grid">
              <div class="form-group">
                <label>退出应用最低权限</label>
                <CustomDropdown
                  v-model="config.permissions.exitAppMinAccountType"
                  :options="permissionLevelOptions"
                />
              </div>
              <div class="form-group">
                <label>提前解锁最低权限</label>
                <CustomDropdown
                  v-model="config.permissions.earlyUnlockMinAccountType"
                  :options="permissionLevelOptions"
                />
              </div>
            </div>
            
            <h4 style="margin: 20px 0 10px;">边栏功能访问控制</h4>
            <div class="permission-list">
              <div class="permission-item">
                <span class="permission-label">主页</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarHomeMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">锁屏设置</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarLockSettingsMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">下课锁屏设置</span>
                <CustomDropdown
                  v-model="config.permissions.breakTimeLockSettingsMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">课表</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarScheduleMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">应用管理</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarAppManagementMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">网络拦截</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarNetworkInterceptionMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">安全日志</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarSecurityLogsMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">屏幕记录</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarScreenshotHistoryMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">摄像头拍照</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarWebcamHistoryMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">自动化</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarAutomationMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">安全中心</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarSecurityCenterMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">系统设置</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarSettingsMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
              <div class="permission-item">
                <span class="permission-label">关于</span>
                <CustomDropdown
                  v-model="config.permissions.sidebarAboutMinAccountType"
                  :options="permissionLevelOptions"
                  style="width: 150px;"
                />
              </div>
            </div>
          </div>

          <div class="config-section">
            <h3>配置同步</h3>
            <div class="form-group">
              <label>配置检查间隔 (分钟)</label>
              <input 
                type="number" 
                v-model.number="config.syncInterval" 
                class="input-field"
                min="1"
                max="1440"
              >
              <small>设备每隔此时间自动检查并同步配置（1-1440分钟）</small>
            </div>
          </div>
        </div>

        <div v-if="activeTab === 'json'" class="json-editor">
          <textarea 
            v-model="jsonContent" 
            class="json-textarea"
            spellcheck="false"
          ></textarea>
          <div v-if="jsonError" class="json-error">
            {{ jsonError }}
          </div>
        </div>
      </div>
    </div>

    <!-- 账户 2FA 配置对话框 -->
    <div v-if="showAccountTwoFactorDialog" class="dialog-overlay">
      <div class="dialog two-factor-dialog">
        <div class="dialog-header">
          <h3>配置双重验证 (2FA) - {{ currentAccount?.username }}</h3>
          <button class="dialog-close" @click="closeAccountTwoFactorDialog">&times;</button>
        </div>
        <div class="dialog-content">
          <div class="form-group">
            <label>2FA 密钥</label>
            <div class="secret-container" style="display: flex; gap: 8px; align-items: center;">
              <input 
                type="text" 
                v-model="accountTwoFactorSecret" 
                class="input-field"
                readonly
                style="flex: 1;"
              >
              <button class="button-secondary" @click="copyAccountTwoFactorSecret" style="white-space: nowrap;">
                复制
              </button>
            </div>
          </div>
          <div class="form-group">
            <label>扫描二维码</label>
            <div class="qr-code-container">
              <img v-if="accountQrCodeUrl" :src="accountQrCodeUrl" alt="2FA 二维码" class="qr-code">
              <p class="qr-code-hint">使用 Google Authenticator 或 Microsoft Authenticator 扫描此二维码</p>
            </div>
          </div>
          <div class="form-group">
            <label>验证码</label>
            <div style="display: flex; gap: 8px; align-items: center;">
              <input 
                type="text" 
                v-model="accountTwoFactorCode" 
                class="input-field"
                placeholder="输入 6 位数验证码"
                style="flex: 1;"
                maxlength="6"
                pattern="[0-9]{6}"
              >
              <button class="button-primary" @click="verifyAccountTwoFactorCode" :disabled="!accountTwoFactorCode">
                验证
              </button>
            </div>
            <small v-if="accountVerificationMessage" :class="{ 'error': !accountVerificationSuccess }">
              {{ accountVerificationMessage }}
            </small>
          </div>
          
          <div class="form-group">
            <label>登录验证模式</label>
            <CustomDropdown
              v-model="currentAccount.loginVerificationMode"
              :options="accountLoginVerificationModeOptions"
              placeholder="选择验证模式"
            />
            <small>选择该账户登录时需要的验证方式</small>
          </div>
          
          <div class="form-group">
            <label>配置说明</label>
            <ul class="instructions">
              <li>1. 打开您的身份验证器应用（如 Google Authenticator 或 Microsoft Authenticator）</li>
              <li>2. 点击 "添加账户" 或 "+" 按钮</li>
              <li>3. 选择 "扫描二维码" 并扫描上方的二维码</li>
              <li>4. 应用将生成一个 6 位数的验证码</li>
              <li>5. 在上方输入框中输入此验证码并点击"验证"按钮</li>
              <li>6. 验证成功后，2FA 配置将对该账户生效</li>
            </ul>
          </div>
        </div>
        <div class="dialog-footer" style="padding: 0 20px 20px;">
          <button class="button-primary" @click="closeAccountTwoFactorDialog" :disabled="!accountVerificationSuccess">
            完成
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, inject } from 'vue'
import { organizationAPI } from '@/api/organization'
import QRCode from 'qrcode'
import { OTP } from 'otplib'
import CustomDropdown from '@/components/CustomDropdown.vue'
import FluentIcons from '@/components/FluentIcons.vue'
import { notify } from '@/utils/notification'

const showNotification = inject('showNotification')

// 生成二维码的函数
const generateQRCode = async (text) => {
  try {
    // 使用 qrcode 库生成二维码
    const dataUrl = await QRCode.toDataURL(text, {
      width: 200,
      margin: 2,
      color: {
        dark: '#000000',
        light: '#ffffff'
      }
    })
    return dataUrl
  } catch (error) {
    console.error('生成二维码失败:', error)
    // 如果生成失败，返回一个默认的占位符
    const canvas = document.createElement('canvas')
    const ctx = canvas.getContext('2d')
    const size = 200
    canvas.width = size
    canvas.height = size
    ctx.fillStyle = '#ffffff'
    ctx.fillRect(0, 0, size, size)
    ctx.fillStyle = '#000000'
    ctx.font = '12px Arial'
    ctx.fillText('QR Code', size/2 - 20, size/2)
    ctx.fillText('Error', size/2 - 10, size/2 + 20)
    return canvas.toDataURL('image/png')
  }
}

// 下拉框选项数据
const orgOptions = computed(() => {
  return organizations.value.map(org => ({
    value: org.id,
    label: org.name,
    iconName: 'building'
  }))
})

const accountTypeOptions = [
  { value: 0, label: '普通用户', iconName: 'user' },
  { value: 1, label: '管理员', iconName: 'admin' },
  { value: 2, label: '超级管理员', iconName: 'super_admin' }
]

const loginVerificationModeOptions = [
  { value: 0, label: '仅密码', iconName: 'key' },
  { value: 1, label: '密码 + 双重验证码（全部都要）', iconName: 'lock' },
  { value: 2, label: '密码 或 双重验证码（任意其一）', iconName: 'lock' },
  { value: 3, label: '仅双重验证码', iconName: 'shield' }
]

const permissionLevelOptions = [
  { value: 0, label: '无', iconName: 'ban' },
  { value: 1, label: '普通用户', iconName: 'user' },
  { value: 2, label: '管理员', iconName: 'admin' },
  { value: 3, label: '超级管理员', iconName: 'super_admin' }
]

const accountLoginVerificationModeOptions = [
  { value: 0, label: '密码 + 双重验证码（全部都要）', iconName: 'lock' },
  { value: 1, label: '密码 或 双重验证码（任意其一）', iconName: 'lock' },
  { value: 2, label: '仅密码', iconName: 'key' },
  { value: 3, label: '仅双重验证码', iconName: 'shield' }
]

const organizations = ref([])
const selectedOrg = ref('')
const activeTab = ref('visual')
const jsonError = ref('')

const defaultConfig = {
  admin: {
    adminUsername: 'admin',
    password: ''
  },
  accounts: [],
  security: {
    isTwoFactorEnabled: false,
    twoFactorSecret: '',
    loginVerificationMode: 0
  },
  permissions: {
    exitAppMinAccountType: 0,
    sidebarHomeMinAccountType: 0,
    sidebarLockSettingsMinAccountType: 0,
    breakTimeLockSettingsMinAccountType: 0,
    sidebarScheduleMinAccountType: 0,
    sidebarAppManagementMinAccountType: 0,
    sidebarNetworkInterceptionMinAccountType: 0,
    sidebarSecurityLogsMinAccountType: 0,
    sidebarScreenshotHistoryMinAccountType: 0,
    sidebarWebcamHistoryMinAccountType: 0,
    sidebarAutomationMinAccountType: 0,
    sidebarSecurityCenterMinAccountType: 0,
    sidebarSettingsMinAccountType: 0,
    sidebarAboutMinAccountType: 0,
    earlyUnlockMinAccountType: 0
  },
  syncInterval: 30
}

const config = ref(JSON.parse(JSON.stringify(defaultConfig)))
const showPassword = ref(false)

const jsonContent = computed({
  get: () => {
    return JSON.stringify(config.value, null, 2)
  },
  set: (val) => {
    try {
      config.value = JSON.parse(val)
      jsonError.value = ''
    } catch (e) {
      jsonError.value = 'JSON 格式错误：' + e.message
    }
  }
})

const qrCodeUrl = ref('')

// 账户 2FA 配置相关
const showAccountTwoFactorDialog = ref(false)
const currentAccountIndex = ref(-1)
const currentAccount = ref(null)
const accountTwoFactorSecret = ref('')
const accountQrCodeUrl = ref('')
const accountTwoFactorCode = ref('')
const accountVerificationSuccess = ref(false)
const accountVerificationMessage = ref('')

// 显示账户 2FA 配置对话框
const showAccountTwoFactorConfig = (index) => {
  currentAccountIndex.value = index
  currentAccount.value = config.value.accounts[index]
  accountTwoFactorSecret.value = currentAccount.value.twoFactorSecret
  accountTwoFactorCode.value = ''
  accountVerificationSuccess.value = false
  accountVerificationMessage.value = ''
  
  // 生成二维码
  generateAccountTwoFactorQRCode()
  
  showAccountTwoFactorDialog.value = true
}

// 关闭账户 2FA 配置对话框
const closeAccountTwoFactorDialog = () => {
  showAccountTwoFactorDialog.value = false
  currentAccountIndex.value = -1
  currentAccount.value = null
  accountTwoFactorSecret.value = ''
  accountQrCodeUrl.value = ''
  accountTwoFactorCode.value = ''
  accountVerificationSuccess.value = false
  accountVerificationMessage.value = ''
}

// 生成账户 2FA 二维码
const generateAccountTwoFactorQRCode = async () => {
  if (!accountTwoFactorSecret.value || !currentAccount.value) {
    accountQrCodeUrl.value = ''
    return
  }
  
  try {
    const username = currentAccount.value.username
    const secret = accountTwoFactorSecret.value
    const issuer = 'ClassScreenLock'
    const label = `${issuer}:${username}`
    const otpUrl = `otpauth://totp/${encodeURIComponent(label)}?secret=${encodeURIComponent(secret)}&issuer=${encodeURIComponent(issuer)}`
    
    // 使用 qrcode 库生成二维码
    const dataUrl = await QRCode.toDataURL(otpUrl, {
      width: 200,
      margin: 2,
      color: {
        dark: '#000000',
        light: '#ffffff'
      }
    })
    accountQrCodeUrl.value = dataUrl
  } catch (error) {
    console.error('生成二维码失败:', error)
    accountQrCodeUrl.value = ''
  }
}

// 复制账户 2FA 密钥
const copyAccountTwoFactorSecret = async () => {
  try {
    await navigator.clipboard.writeText(accountTwoFactorSecret.value)
    if (showNotification) {
      showNotification('密钥已复制到剪贴板', 'success')
    } else {
      notify.success('密钥已复制到剪贴板')
    }
  } catch (err) {
    const textArea = document.createElement('textarea')
    textArea.value = accountTwoFactorSecret.value
    document.body.appendChild(textArea)
    textArea.select()
    document.execCommand('copy')
    document.body.removeChild(textArea)
    if (showNotification) {
      showNotification('密钥已复制到剪贴板', 'success')
    } else {
      notify.success('密钥已复制到剪贴板')
    }
  }
}

// 验证账户 2FA 验证码
const verifyAccountTwoFactorCode = async () => {
  if (!accountTwoFactorCode.value || accountTwoFactorCode.value.length !== 6) {
    accountVerificationMessage.value = '请输入 6 位数字验证码'
    accountVerificationSuccess.value = false
    return
  }
  
  try {
    const secret = accountTwoFactorSecret.value
    if (!secret) {
      accountVerificationMessage.value = '请先生成 2FA 密钥'
      accountVerificationSuccess.value = false
      return
    }
    
    const authenticator = new OTP({ strategy: 'totp' })
    
    // 验证验证码
    const isValid = await authenticator.verify({ secret, token: accountTwoFactorCode.value })
    
    if (isValid) {
      accountVerificationMessage.value = '验证成功！2FA 配置已生效'
      accountVerificationSuccess.value = true
      // 更新账户的 2FA 状态
      if (currentAccountIndex.value !== -1) {
        config.value.accounts[currentAccountIndex.value].isTwoFactorEnabled = true
        config.value.accounts[currentAccountIndex.value].twoFactorSecret = accountTwoFactorSecret.value
      }
      // 清除验证码输入
      accountTwoFactorCode.value = ''
    } else {
      accountVerificationMessage.value = '验证码无效，请重新输入'
      accountVerificationSuccess.value = false
    }
  } catch (error) {
    console.error('验证验证码失败:', error)
    accountVerificationMessage.value = '验证过程中出现错误'
    accountVerificationSuccess.value = false
  }
}

// 生成账户 2FA 密钥
const generateAccountSecret = (index) => {
  if (config.value.accounts && config.value.accounts[index]) {
    config.value.accounts[index].twoFactorSecret = generateRandomSecret()
  }
}

// 复制账户 2FA 密钥
const copyAccountSecret = (index) => {
  const secret = config.value.accounts[index].twoFactorSecret
  navigator.clipboard.writeText(secret)
    .then(() => {
      if (showNotification) {
        showNotification('密钥已复制到剪贴板', 'success')
      } else {
        notify.success('密钥已复制到剪贴板')
      }
    })
    .catch(err => {
      console.error('复制失败:', err)
      if (showNotification) {
        showNotification('复制失败，请手动复制', 'error')
      } else {
        notify.error('复制失败，请手动复制')
      }
    })
}

watch(activeTab, (newTab) => {
  if (newTab === 'json') {
    jsonError.value = ''
  }
})

// 当 2FA 禁用时，自动将验证模式设置为 PasswordOnly
watch(() => config.value.security.isTwoFactorEnabled, (newValue) => {
  if (!newValue) {
    config.value.security.loginVerificationMode = 0
  }
})

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
    const data = await organizationAPI.getSecurityConfig(selectedOrg.value)
    if (data) {
      // 深度合并默认配置和服务器配置
      config.value = {
        ...defaultConfig,
        ...data,
        permissions: {
          ...defaultConfig.permissions,
          ...(data.permissions || {})
        }
      }
    } else {
      config.value = JSON.parse(JSON.stringify(defaultConfig))
    }
  } catch (error) {
    console.error('Failed to load config:', error)
    config.value = JSON.parse(JSON.stringify(defaultConfig))
  }
}

const saveConfig = async () => {
  if (!selectedOrg.value) return
  
  try {
    await organizationAPI.updateSecurityConfig(selectedOrg.value, config.value)
    if (showNotification) {
      showNotification('配置保存成功！', 'success')
    } else {
      notify.success('配置保存成功！')
    }
  } catch (error) {
    console.error('Failed to save config:', error)
    if (showNotification) {
      showNotification('保存失败，请重试', 'error')
    } else {
      notify.error('保存失败，请重试')
    }
  }
}

const addAccount = () => {
  if (!config.value.accounts) {
    config.value.accounts = []
  }
  if (config.value.accounts.length < 5) {
    config.value.accounts.push({
      username: '',
      password: '',
      accountType: 0,
      id: '',
      isTwoFactorEnabled: false,
      twoFactorSecret: '',
      loginVerificationMode: 0
    })
  }
}

const removeAccount = (index) => {
  if (config.value.accounts && config.value.accounts.length > 0) {
    config.value.accounts.splice(index, 1)
  }
}

const generateNewSecret = () => {
  config.value.security.twoFactorSecret = generateRandomSecret()
}

// 生成随机 2FA 密钥
const generateRandomSecret = () => {
  const charset = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'
  let secret = ''
  for (let i = 0; i < 32; i++) {
    secret += charset.charAt(Math.floor(Math.random() * charset.length))
  }
  return secret
}

const copySecret = async () => {
  const secret = config.value.security.twoFactorSecret
  try {
    await navigator.clipboard.writeText(secret)
    if (showNotification) {
      showNotification('密钥已复制到剪贴板', 'success')
    } else {
      notify.success('密钥已复制到剪贴板')
    }
  } catch (err) {
    // 降级方案
    const textArea = document.createElement('textarea')
    textArea.value = secret
    document.body.appendChild(textArea)
    textArea.select()
    document.execCommand('copy')
    document.body.removeChild(textArea)
    if (showNotification) {
      showNotification('密钥已复制到剪贴板', 'success')
    } else {
      notify.success('密钥已复制到剪贴板')
    }
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
.security-config-page {
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
  font-size: 1.25em;
  font-weight: 600;
}

.header-actions {
  display: flex;
  gap: 12px;
  align-items: center;
}

.config-container {
  background: var(--fui-surface);
  border: 1px solid var(--fui-border);
  border-radius: 8px;
  padding: 24px;
  min-height: 600px;
  font-size: var(--fui-font-size-base);
}

.empty-state {
  text-align: center;
  padding: 60px 20px;
  color: var(--fui-text-secondary);
  font-size: 1em;
}

.config-editor {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.editor-tabs {
  display: flex;
  gap: 8px;
  border-bottom: 1px solid var(--fui-border);
  padding-bottom: 12px;
}

.tab-button {
  padding: 8px 16px;
  background: none;
  border: 1px solid transparent;
  border-radius: 6px;
  cursor: pointer;
  font-size: 1em;
  color: var(--fui-text);
  transition: all 0.2s;
}

.tab-button:hover {
  background: var(--fui-background);
}

.tab-button.active {
  background: var(--fui-primary);
  color: white;
}

.visual-editor {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.config-section {
  padding: 20px;
  background: var(--fui-surface);
  border: 1px solid var(--fui-border);
  border-radius: 8px;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  opacity: 0;
  transform: translateY(20px);
  animation: sectionFadeIn 0.6s cubic-bezier(0.4, 0, 0.2, 1);
  animation-fill-mode: both;
}

.config-section:nth-child(1) { animation-delay: 0.1s; }
.config-section:nth-child(2) { animation-delay: 0.15s; }
.config-section:nth-child(3) { animation-delay: 0.2s; }
.config-section:nth-child(4) { animation-delay: 0.25s; }

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

.config-section h3 {
  margin: 0 0 16px 0;
  color: var(--fui-primary);
  font-size: 1em;
  font-weight: 600;
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
  display: block;
  margin-bottom: 6px;
  font-weight: 500;
  color: var(--fui-text);
  font-size: 0.875em;
}

.form-group input[type="checkbox"] {
  margin-right: 8px;
}

.form-group small {
  display: block;
  margin-top: 4px;
  color: var(--fui-text-secondary);
  font-size: 0.75em;
}

.form-group small.error {
  color: var(--fui-error);
}

.form-group small.success {
  color: var(--fui-success);
}

textarea.input-field {
  resize: vertical;
  font-family: 'Consolas', 'Courier New', monospace;
}

.json-editor {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.json-textarea {
  width: 100%;
  min-height: 500px;
  padding: 16px;
  border: 1px solid var(--fui-border);
  border-radius: 8px;
  font-family: 'Consolas', 'Courier New', monospace;
  font-size: 13px;
  line-height: 1.5;
  resize: vertical;
  background: #f8f9fa;
}

.json-textarea:focus {
  outline: 2px solid var(--fui-primary);
  border-color: transparent;
}

.json-error {
  color: #a80000;
  font-size: 13px;
  padding: 8px;
  background: #fde7e9;
  border-radius: 6px;
}

button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Win UI3 风格按钮 */
.button-primary {
  padding: 8px 16px;
  background: var(--fui-primary);
  color: white;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
  transition: all 0.2s;
}

.button-primary:hover {
  background: var(--fui-primary-hover);
}

.button-primary:disabled {
  background: var(--fui-border);
  color: var(--fui-text-secondary);
  cursor: not-allowed;
}

/* Win UI3 风格输入框 */
.input-field {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--fui-border);
  border-radius: 6px;
  font-size: 14px;
  background: var(--fui-surface);
  color: var(--fui-text);
  transition: all 0.2s;
}

.input-field:focus {
  outline: 2px solid var(--fui-primary);
  border-color: transparent;
}

/* Win UI3 风格下拉框 */
.input-field select {
  appearance: none;
  background-image: url('data:image/svg+xml;utf8,<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>');
  background-repeat: no-repeat;
  background-position: right 12px center;
  padding-right: 32px;
}

/* Win UI3 风格复选框 */
input[type="checkbox"] {
  width: 16px;
  height: 16px;
  accent-color: var(--fui-primary);
}

/* 账户管理样式 */
.accounts-list {
  margin-top: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  border: 1px solid var(--fui-border);
  border-radius: 8px;
  overflow: hidden;
  background: var(--fui-surface);
}

.account-item {
  display: flex;
  gap: 12px;
  align-items: center;
  padding: 16px;
  border-bottom: 1px solid var(--fui-border);
  transition: all 0.2s;
}

.account-item:last-child {
  border-bottom: none;
}

.account-item:hover {
  background: var(--fui-background);
  box-shadow: inset 0 0 0 1px var(--fui-primary);
}

.account-fields {
  flex: 1;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.empty-accounts {
  padding: 40px 20px;
  text-align: center;
  color: var(--fui-text-secondary);
  background: var(--fui-background);
  border: 1px dashed var(--fui-border);
  border-radius: 8px;
  margin-top: 16px;
  font-size: 14px;
}

/* Win UI3 风格按钮 */
.button-danger {
  padding: 8px 16px;
  background: #e74c3c;
  color: white;
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-size: 14px;
  font-weight: 500;
  transition: all 0.2s;
  align-self: center;
}

.button-danger:hover {
  background: #c0392b;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.button-danger:disabled {
  background: var(--fui-border);
  color: var(--fui-text-secondary);
  cursor: not-allowed;
  box-shadow: none;
}

/* 二维码样式 */
.qr-code-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  margin-top: 12px;
  padding: 16px;
  background: var(--fui-surface);
  border: 1px solid var(--fui-border);
  border-radius: 8px;
  max-width: 300px;
}

.qr-code {
  width: 200px;
  height: 200px;
  margin-bottom: 12px;
  border: 1px solid var(--fui-border);
  border-radius: 8px;
}

.qr-code-hint {
  text-align: center;
  color: var(--fui-text-secondary);
  font-size: 14px;
  margin: 0;
}

/* 表单操作按钮组 */
.form-actions {
  display: flex;
  gap: 8px;
  margin-top: 8px;
}

/* 对话框样式 */
.dialog-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.dialog {
  background: var(--fui-surface);
  border-radius: 8px;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
  width: 90%;
  max-width: 600px;
  max-height: 90vh;
  overflow-y: auto;
}

.dialog-content {
  padding: 20px;
}

/* 2FA配置对话框样式 */
.two-factor-dialog .dialog-content {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.two-factor-dialog .qr-code-container {
  max-width: 100%;
  margin: 20px 0;
}

.two-factor-dialog .qr-code {
  width: 250px;
  height: 250px;
}

.dialog-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px;
  border-bottom: 1px solid var(--fui-border);
}

.dialog-header h3 {
  margin: 0;
  color: var(--fui-text-primary);
}

.dialog-close {
  background: none;
  border: none;
  font-size: 24px;
  cursor: pointer;
  color: var(--fui-text-secondary);
  padding: 0;
  width: 32px;
  height: 32px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 4px;
  transition: background-color 0.2s;
}

.dialog-close:hover {
  background: var(--fui-background);
}

.dialog-content {
  padding: 16px;
}

.dialog-footer {
  padding: 16px;
  border-top: 1px solid var(--fui-border);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

/* 密钥容器 */
.secret-container {
  display: flex;
  gap: 8px;
  align-items: center;
}

.secret-container .input-field {
  flex: 1;
}

/* 指令列表 */
.instructions {
  padding-left: 20px;
  margin: 8px 0 0 0;
  color: var(--fui-text-secondary);
}

.instructions li {
  margin-bottom: 4px;
}

/* 次要按钮 */
.button-secondary {
  background: var(--fui-background);
  color: var(--fui-text-primary);
  border: 1px solid var(--fui-border);
  padding: 8px 16px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 14px;
  transition: all 0.2s;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.1);
}

.button-secondary:hover {
  background: var(--fui-surface);
  border-color: var(--fui-primary);
}

/* 权限列表样式 */
.permission-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  background: var(--fui-background);
  border-radius: 8px;
}

.permission-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: var(--fui-surface);
  border: 1px solid var(--fui-border);
  border-radius: 6px;
  transition: all 0.2s;
}

.permission-item:hover {
  background: var(--fui-surface-hover);
  border-color: var(--fui-primary);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.05);
}

.permission-label {
  font-weight: 500;
  color: var(--fui-text);
  font-size: 14px;
}

.password-input-container {
  position: relative;
  width: 100%;
}

.password-input {
  padding-right: 40px;
}

.password-toggle {
  position: absolute;
  right: 8px;
  top: 50%;
  transform: translateY(-50%);
  background: none;
  border: none;
  cursor: pointer;
  color: var(--fui-text-secondary);
  padding: 4px;
  border-radius: 4px;
  transition: all 0.2s ease;
}

.password-toggle:hover {
  background: var(--fui-background-hover);
  color: var(--fui-text);
}
</style>
