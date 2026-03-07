<template>
  <div class="dashboard">
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon">
          <div class="icon-glow"></div>
          <FluentIcons name="organization" :size="28" />
        </div>
        <div class="stat-info">
          <h3 class="count-up">{{ animatedStats.organizations }}</h3>
          <p>组织数量</p>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon">
          <div class="icon-glow"></div>
          <FluentIcons name="device" :size="28" />
        </div>
        <div class="stat-info">
          <h3 class="count-up">{{ animatedStats.devices }}</h3>
          <p>设备数量</p>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon">
          <div class="icon-glow"></div>
          <FluentIcons name="online" :size="28" />
        </div>
        <div class="stat-info">
          <h3 class="count-up">{{ animatedStats.onlineDevices }}</h3>
          <p>在线设备</p>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon">
          <div class="icon-glow"></div>
          <FluentIcons name="security" :size="28" />
        </div>
        <div class="stat-info">
          <h3 class="count-up">{{ animatedStats.securityPolicies }}</h3>
          <p>安全策略</p>
        </div>
      </div>
    </div>

    <div class="recent-activity card">
      <h2 class="section-title">
        <span class="title-glow"></span>
        最近活动
      </h2>
      <div class="activity-list">
        <div v-for="activity in recentActivities" :key="activity.id" class="activity-item">
          <div class="activity-indicator"></div>
          <div class="activity-time">{{ activity.time }}</div>
          <div class="activity-content">{{ activity.content }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted, onUnmounted, watch } from 'vue'
import { organizationAPI, deviceAPI } from '@/api/organization'
import FluentIcons from '@/components/FluentIcons.vue'

const stats = ref({
  organizations: 0,
  devices: 0,
  onlineDevices: 0,
  securityPolicies: 0
})

const animatedStats = reactive({
  organizations: 0,
  devices: 0,
  onlineDevices: 0,
  securityPolicies: 0
})

const recentActivities = ref([
])

let refreshInterval = null
let animationFrameId = null

const animateValue = (start, end, duration, onUpdate) => {
  const startTime = performance.now()
  
  const animate = (currentTime) => {
    const elapsed = currentTime - startTime
    const progress = Math.min(elapsed / duration, 1)
    
    const easeOutQuart = 1 - Math.pow(1 - progress, 4)
    const currentValue = Math.floor(start + (end - start) * easeOutQuart)
    
    onUpdate(currentValue)
    
    if (progress < 1) {
      animationFrameId = requestAnimationFrame(animate)
    }
  }
  
  animationFrameId = requestAnimationFrame(animate)
}

const animateStats = (newStats) => {
  Object.keys(newStats).forEach(key => {
    const startValue = animatedStats[key]
    const endValue = newStats[key]
    
    if (startValue !== endValue) {
      animateValue(startValue, endValue, 1200, (value) => {
        animatedStats[key] = value
      })
    }
  })
}

const loadStats = async () => {
  try {
    const [orgs, devices] = await Promise.all([
      organizationAPI.getAll(),
      deviceAPI.getAll()
    ])
    const newStats = {
      organizations: orgs.length || 0,
      devices: devices.length || 0,
      onlineDevices: devices.filter(d => d.status === 'online').length || 0,
      securityPolicies: 0
    }
    
    animateStats(newStats)
  } catch (error) {
    console.error('Failed to load stats:', error)
    animateStats({
      organizations: 0,
      devices: 0,
      onlineDevices: 0,
      securityPolicies: 0
    })
  }
}

const startAutoRefresh = () => {
  if (refreshInterval) return
  refreshInterval = setInterval(async () => {
    await loadStats()
  }, 2000)
}

const stopAutoRefresh = () => {
  if (refreshInterval) {
    clearInterval(refreshInterval)
    refreshInterval = null
  }
  if (animationFrameId) {
    cancelAnimationFrame(animationFrameId)
    animationFrameId = null
  }
}

onMounted(async () => {
  await loadStats()
  startAutoRefresh()
})

onUnmounted(() => {
  stopAutoRefresh()
})
</script>

<style scoped>
.dashboard {
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

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 20px;
}

.stat-card {
  animation: cardFadeIn 0.6s cubic-bezier(0.34, 1.56, 0.64, 1);
  animation-fill-mode: both;
}

.stat-card:nth-child(1) { animation-delay: 0.1s; }
.stat-card:nth-child(2) { animation-delay: 0.2s; }
.stat-card:nth-child(3) { animation-delay: 0.3s; }
.stat-card:nth-child(4) { animation-delay: 0.4s; }

@keyframes cardFadeIn {
  from {
    opacity: 0;
    transform: translateY(30px) scale(0.95);
  }
  to {
    opacity: 1;
    transform: translateY(0) scale(1);
  }
}

.stat-card {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: var(--fui-radius);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
  padding: 24px;
  display: flex;
  align-items: center;
  gap: 16px;
  transition: all 0.4s cubic-bezier(0.4, 0, 0.2, 1);
  opacity: 0.9;
  position: relative;
  overflow: hidden;
}

.stat-card::before {
  content: '';
  position: absolute;
  top: 0;
  left: -100%;
  width: 100%;
  height: 100%;
  background: linear-gradient(
    90deg,
    transparent,
    rgba(255, 255, 255, 0.1),
    transparent
  );
  transition: left 0.6s cubic-bezier(0.4, 0, 0.2, 1);
}

.stat-card:hover::before {
  left: 100%;
}

.stat-card:hover {
  transform: translateY(-8px) scale(1.02);
  opacity: 1;
  box-shadow: 0 16px 48px rgba(0, 0, 0, 0.2), 0 0 20px rgba(255, 255, 255, 0.1);
  border-color: rgba(255, 255, 255, 0.3);
}

.stat-icon {
  font-size: 40px;
  width: 60px;
  height: 60px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(255, 255, 255, 0.2);
  backdrop-filter: blur(10px);
  -webkit-backdrop-filter: blur(10px);
  border: 1px solid rgba(255, 255, 255, 0.3);
  border-radius: 50%;
  transition: all 0.5s cubic-bezier(0.34, 1.56, 0.64, 1);
  position: relative;
  overflow: visible;
}

.icon-glow {
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: 100%;
  height: 100%;
  border-radius: 50%;
  background: radial-gradient(
    circle,
    rgba(255, 255, 255, 0.4) 0%,
    transparent 70%
  );
  opacity: 0;
  animation: glowPulse 3s cubic-bezier(0.4, 0, 0.6, 1) infinite;
}

@keyframes glowPulse {
  0%, 100% {
    transform: translate(-50%, -50%) scale(0.8);
    opacity: 0;
  }
  50% {
    transform: translate(-50%, -50%) scale(1.5);
    opacity: 0.6;
  }
}

.stat-card:hover .stat-icon {
  transform: scale(1.15);
  background: rgba(255, 255, 255, 0.3);
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.15), 0 0 16px rgba(255, 255, 255, 0.3);
}

.stat-card:hover .icon-glow {
  animation: glowPulseIntense 1.5s cubic-bezier(0.4, 0, 0.6, 1) infinite;
}

@keyframes glowPulseIntense {
  0%, 100% {
    transform: translate(-50%, -50%) scale(0.8);
    opacity: 0;
  }
  50% {
    transform: translate(-50%, -50%) scale(1.8);
    opacity: 0.8;
  }
}

.stat-info h3 {
  font-size: 2.25em;
  font-weight: 700;
  color: var(--fui-text);
  margin: 0 0 4px 0;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative;
}

.count-up {
  animation: numberPop 0.3s cubic-bezier(0.34, 1.56, 0.64, 1);
}

@keyframes numberPop {
  0% {
    transform: scale(0.9);
    opacity: 0.7;
  }
  50% {
    transform: scale(1.1);
  }
  100% {
    transform: scale(1);
    opacity: 1;
  }
}

.stat-card:hover .stat-info h3 {
  transform: scale(1.05);
  text-shadow: 0 0 20px rgba(255, 255, 255, 0.3);
}

.stat-info p {
  font-size: 0.94em;
  color: var(--fui-text-secondary);
  margin: 0;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.stat-card:hover .stat-info p {
  color: var(--fui-text);
  letter-spacing: 0.3px;
}

.recent-activity {
  background: rgba(255, 255, 255, 0.1);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: var(--fui-radius);
  box-shadow: 0 8px 32px rgba(0, 0, 0, 0.1);
  padding: 24px;
  animation: containerFadeIn 0.6s cubic-bezier(0.4, 0, 0.2, 1);
  animation-delay: 0.5s;
  animation-fill-mode: both;
  position: relative;
  overflow: hidden;
  font-size: var(--fui-font-size-base);
}

.recent-activity::after {
  content: '';
  position: absolute;
  bottom: 0;
  left: 0;
  width: 100%;
  height: 2px;
  background: linear-gradient(
    90deg,
    transparent,
    rgba(255, 255, 255, 0.3),
    transparent
  );
  animation: shimmer 3s cubic-bezier(0.4, 0, 0.2, 1) infinite;
}

@keyframes shimmer {
  0% {
    transform: translateX(-100%);
  }
  100% {
    transform: translateX(100%);
  }
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

.section-title {
  font-size: 1.5em;
  font-weight: 700;
  margin-bottom: 20px;
  color: var(--fui-text);
  display: flex;
  align-items: center;
  gap: 12px;
  position: relative;
}

.title-glow {
  width: 8px;
  height: 8px;
  background: linear-gradient(135deg, rgba(255, 255, 255, 0.8), rgba(255, 255, 255, 0.2));
  border-radius: 50%;
  animation: titleGlow 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
  box-shadow: 0 0 12px rgba(255, 255, 255, 0.5);
}

@keyframes titleGlow {
  0%, 100% {
    transform: scale(1);
    opacity: 0.8;
  }
  50% {
    transform: scale(1.3);
    opacity: 1;
    box-shadow: 0 0 20px rgba(255, 255, 255, 0.8);
  }
}

.recent-activity h2 {
  font-size: 20px;
  font-weight: 600;
  margin-bottom: 20px;
  color: var(--fui-text);
}

.activity-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.activity-item {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 12px 0;
  border-bottom: 1px solid rgba(255, 255, 255, 0.1);
  transition: all 0.4s cubic-bezier(0.4, 0, 0.2, 1);
  opacity: 0;
  transform: translateX(-20px);
  animation: activityFadeIn 0.6s cubic-bezier(0.4, 0, 0.2, 1);
  animation-fill-mode: both;
  position: relative;
}

.activity-item:nth-child(1) { animation-delay: 0.6s; }
.activity-item:nth-child(2) { animation-delay: 0.7s; }
.activity-item:nth-child(3) { animation-delay: 0.8s; }
.activity-item:nth-child(4) { animation-delay: 0.9s; }

.activity-indicator {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: linear-gradient(135deg, rgba(255, 255, 255, 0.8), rgba(255, 255, 255, 0.3));
  flex-shrink: 0;
  animation: indicatorPulse 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
  position: relative;
}

.activity-indicator::before {
  content: '';
  position: absolute;
  top: 50%;
  left: 50%;
  transform: translate(-50%, -50%);
  width: 100%;
  height: 100%;
  border-radius: 50%;
  background: rgba(255, 255, 255, 0.4);
  animation: indicatorRipple 2s cubic-bezier(0.4, 0, 0.6, 1) infinite;
}

@keyframes indicatorPulse {
  0%, 100% {
    transform: scale(1);
    opacity: 0.8;
  }
  50% {
    transform: scale(1.2);
    opacity: 1;
  }
}

@keyframes indicatorRipple {
  0% {
    transform: translate(-50%, -50%) scale(1);
    opacity: 0.6;
  }
  100% {
    transform: translate(-50%, -50%) scale(2.5);
    opacity: 0;
  }
}

@keyframes activityFadeIn {
  from {
    opacity: 0;
    transform: translateX(-20px);
  }
  to {
    opacity: 0.8;
    transform: translateX(0);
  }
}

.activity-item:hover {
  background: rgba(255, 255, 255, 0.05);
  padding-left: 16px;
  padding-right: 16px;
  padding-top: 16px;
  padding-bottom: 16px;
  border-radius: 8px;
  opacity: 1;
  transform: translateX(8px);
  border-bottom-color: transparent;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.1);
}

.activity-item:hover .activity-indicator {
  animation: indicatorPulseIntense 1s cubic-bezier(0.4, 0, 0.6, 1) infinite;
}

@keyframes indicatorPulseIntense {
  0%, 100% {
    transform: scale(1.2);
    opacity: 1;
  }
  50% {
    transform: scale(1.5);
    opacity: 1;
    box-shadow: 0 0 12px rgba(255, 255, 255, 0.6);
  }
}

.activity-time {
  font-size: 0.81em;
  color: var(--fui-text-secondary);
  flex-shrink: 0;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.activity-content {
  font-size: 0.94em;
  color: var(--fui-text);
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
}

.activity-item:hover .activity-time {
  color: var(--fui-text);
  letter-spacing: 0.2px;
}

.activity-item:hover .activity-content {
  font-weight: 500;
  letter-spacing: 0.2px;
}
</style>
