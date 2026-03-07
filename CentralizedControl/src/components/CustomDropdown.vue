<template>
  <div class="custom-dropdown" ref="dropdownRef">
    <div class="dropdown-btn" @click="toggleDropdown">
      <FluentIcons v-if="selectedIconName" :name="selectedIconName" :size="18" />
      <span class="dropdown-label">{{ selectedLabel }}</span>
      <span class="arrow">
        <FluentIcons :name="isOpen ? 'arrow_up' : 'arrow_down'" :size="14" />
      </span>
    </div>

    <teleport to="body" v-if="isOpen">
      <ul 
        class="dropdown-panel" 
        :style="panelStyle"
        @click="handlePanelClick"
      >
        <li
          v-for="option in options"
          :key="option.value"
          :class="{ selected: modelValue === option.value }"
          @click="selectOption(option)"
        >
          <FluentIcons v-if="option.iconName" :name="option.iconName" :size="18" />
          <span>{{ option.label }}</span>
        </li>
      </ul>
    </teleport>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted, nextTick } from 'vue'
import FluentIcons from './FluentIcons.vue'

const props = defineProps({
  modelValue: {
    type: [String, Number],
    default: ''
  },
  options: {
    type: Array,
    required: true,
    default: () => []
  },
  placeholder: {
    type: String,
    default: '请选择'
  }
})

const emit = defineEmits(['update:modelValue'])

const isOpen = ref(false)
const dropdownRef = ref(null)

const selectedOption = computed(() => {
  const found = props.options.find(opt => opt.value === props.modelValue)
  return found !== undefined ? found : null
})

const selectedLabel = computed(() => {
  if (selectedOption.value) {
    return selectedOption.value.label
  }
  // 如果没有找到选项且有 placeholder，显示 placeholder
  return props.placeholder
})

const selectedIconName = computed(() => {
  if (selectedOption.value) {
    return selectedOption.value.iconName
  }
  return null
})

const panelStyle = computed(() => {
  if (!isOpen.value || !dropdownRef.value) {
    return {}
  }
  
  const rect = dropdownRef.value.getBoundingClientRect()
  return {
    position: 'fixed',
    top: `${rect.bottom + window.scrollY}px`,
    left: `${rect.left + window.scrollX}px`,
    width: `${rect.width}px`,
    zIndex: '9999',
  }
})

const toggleDropdown = () => {
  isOpen.value = !isOpen.value
}

const selectOption = (option) => {
  emit('update:modelValue', option.value)
  isOpen.value = false
}

const handlePanelClick = (event) => {
  // 防止点击事件冒泡关闭下拉框
  event.stopPropagation()
}

const handleClickOutside = (event) => {
  // 检查点击的目标是否在下拉框或面板内部
  const isClickInsideDropdown = dropdownRef.value && dropdownRef.value.contains(event.target)
  const isClickOnPanel = event.target.closest('.dropdown-panel')
  
  if (!isClickInsideDropdown && !isClickOnPanel) {
    isOpen.value = false
  }
}

onMounted(() => {
  document.addEventListener('click', handleClickOutside)
})

onUnmounted(() => {
  document.removeEventListener('click', handleClickOutside)
})
</script>

<style scoped>
.custom-dropdown {
  position: relative;
  width: 100%;
  font-family: 'Inter', sans-serif;
  min-width: 180px;
  z-index: 1;
}

[data-theme="dark"] .dropdown-btn {
  background: rgba(255, 255, 255, 0.08);
  border: 1.5px solid rgba(255, 255, 255, 0.15);
  color: var(--fui-text);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.3);
}

.dropdown-btn {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  width: 100%;
  height: 40px;
  padding: 0 1rem;
  background: white;
  border: 1.5px solid #dbe8f2;
  border-radius: 40px;
  font-size: 0.9rem;
  color: #1b3b5c;
  font-weight: 500;
  cursor: pointer;
  transition: all 0.2s;
  box-shadow: 0 4px 12px rgba(31, 73, 125, 0.08);
  backdrop-filter: blur(4px);
  margin-bottom: 0.5rem;
}

[data-theme="dark"] .dropdown-btn :deep(.fluent-icon:first-child) {
  color: var(--fui-primary);
}

.dropdown-btn :deep(.fluent-icon:first-child) {
  color: #3f80c5;
  font-size: 1rem;
  width: 1.5rem;
  text-align: center;
}

.dropdown-label {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

[data-theme="dark"] .dropdown-btn .arrow {
  background: rgba(255, 255, 255, 0.1);
  color: var(--fui-text);
}

.dropdown-btn .arrow {
  font-size: 0.7rem;
  color: #678fb3;
  background: #ecf3fa;
  padding: 0.25rem 0.6rem;
  border-radius: 30px;
  transition: all 0.2s;
  flex-shrink: 0;
}

[data-theme="dark"] .dropdown-btn:hover {
  border-color: rgba(255, 255, 255, 0.3);
  background: rgba(255, 255, 255, 0.12);
  box-shadow: 0 12px 24px -10px rgba(0, 0, 0, 0.4);
}

.dropdown-btn:hover {
  border-color: #a6c6ec;
  background: #f8fcff;
  box-shadow: 0 12px 24px -10px rgba(43, 96, 164, 0.25);
}

[data-theme="dark"] .dropdown-btn:hover .arrow {
  background: rgba(255, 255, 255, 0.2);
  color: var(--fui-text);
}

.dropdown-btn:hover .arrow {
  background: #d5e6fa;
  color: #215f9e;
}

[data-theme="dark"] .dropdown-panel {
  background: rgba(30, 30, 30, 0.95);
  border: 1px solid rgba(255, 255, 255, 0.1);
  box-shadow: 0 20px 35px -10px rgba(0, 0, 0, 0.5);
}

.dropdown-panel {
  list-style: none;
  background: white;
  border-radius: 24px;
  padding: 0.5rem 0;
  box-shadow: 0 20px 35px -10px rgba(12, 45, 85, 0.35), 0 0 0 1px rgba(0,0,0,0.02);
  border: 1px solid rgba(255,255,255,0.8);
  backdrop-filter: blur(8px);
  max-height: 300px;
  overflow-y: auto;
  margin-top: 0;
  transition: all 0.2s ease;
  position: absolute;
  width: 100%;
  z-index: 9999;
}

[data-theme="dark"] .dropdown-panel::-webkit-scrollbar-track {
  background: rgba(255, 255, 255, 0.05);
}

.dropdown-panel::-webkit-scrollbar-track {
  background: #eef5fc;
  border-radius: 8px;
  margin: 8px 0;
}

[data-theme="dark"] .dropdown-panel::-webkit-scrollbar-thumb {
  background: rgba(255, 255, 255, 0.2);
}

.dropdown-panel::-webkit-scrollbar-thumb {
  background: #b9cfec;
  border-radius: 8px;
}

[data-theme="dark"] .dropdown-panel::-webkit-scrollbar-thumb:hover {
  background: rgba(255, 255, 255, 0.3);
}

.dropdown-panel::-webkit-scrollbar-thumb:hover {
  background: #8fb4e2;
}

[data-theme="dark"] .dropdown-panel li {
  color: var(--fui-text);
}

.dropdown-panel li {
  padding: 0.6rem 1.2rem;
  display: flex;
  align-items: center;
  gap: 0.8rem;
  font-size: 0.9rem;
  color: #1e3f60;
  cursor: pointer;
  transition: background 0.18s, border 0.1s;
  border-left: 3px solid transparent;
  margin: 0.15rem 0.5rem;
  border-radius: 30px;
}

[data-theme="dark"] .dropdown-panel li :deep(.fluent-icon) {
  color: var(--fui-text-secondary);
}

.dropdown-panel li :deep(.fluent-icon) {
  width: 1.4rem;
  font-size: 1rem;
  color: #4875a3;
  text-align: center;
  transition: color 0.2s;
}

[data-theme="dark"] .dropdown-panel li:hover {
  background: rgba(255, 255, 255, 0.08);
  border-left-color: var(--fui-primary);
}

.dropdown-panel li:hover {
  background: #e9f2fe;
  border-left-color: #3f80c5;
}

[data-theme="dark"] .dropdown-panel li:hover :deep(.fluent-icon) {
  color: var(--fui-text);
}

.dropdown-panel li:hover :deep(.fluent-icon) {
  color: #215f9e;
}

[data-theme="dark"] .dropdown-panel li.selected {
  background: rgba(255, 255, 255, 0.12);
  color: var(--fui-text);
  border-left-color: var(--fui-primary);
  box-shadow: inset 0 1px 3px rgba(255,255,255,0.1), 0 4px 10px rgba(0, 0, 0, 0.3);
}

.dropdown-panel li.selected {
  background: #e1efff;
  font-weight: 500;
  color: #0e3d6b;
  border-left-color: #1e5ea8;
  box-shadow: inset 0 1px 3px rgba(255,255,255,0.8), 0 4px 10px rgba(47, 98, 162, 0.1);
}

[data-theme="dark"] .dropdown-panel li.selected :deep(.fluent-icon) {
  color: var(--fui-primary);
}

.dropdown-panel li.selected :deep(.fluent-icon) {
  color: #1e5ea8;
}
</style>