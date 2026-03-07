<template>
  <div class="custom-switch">
    <label class="switch-container">
      <input 
        type="checkbox" 
        :checked="modelValue" 
        @change="handleChange"
      >
      <span class="switch-slider" :class="{ 'active': modelValue }">
        <span class="switch-handle"></span>
      </span>
      <span class="switch-label" :class="{ 'active': modelValue }">
        {{ modelValue ? 'On' : 'Off' }}
      </span>
    </label>
  </div>
</template>

<script setup>
import { defineProps, defineEmits } from 'vue'

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:modelValue'])

const handleChange = (event) => {
  emit('update:modelValue', event.target.checked)
}
</script>

<style scoped>
.custom-switch {
  display: inline-block;
}

.switch-container {
  display: flex;
  align-items: center;
  gap: 12px;
  cursor: pointer;
  user-select: none;
  position: relative;
}

.switch-container input {
  position: absolute;
  opacity: 0;
  width: 100%;
  height: 100%;
  cursor: pointer;
}

.switch-slider {
  position: relative;
  width: 50px;
  height: 26px;
  background-color: rgba(255, 255, 255, 0.1);
  border: 1px solid rgba(255, 255, 255, 0.2);
  border-radius: 13px;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  flex-shrink: 0;
}

.switch-slider::before {
  content: '';
  position: absolute;
  top: 2px;
  left: 2px;
  width: 20px;
  height: 20px;
  background-color: #ccc;
  border-radius: 50%;
  transition: all 0.3s cubic-bezier(0.4, 0, 0.2, 1);
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.2);
}

.switch-slider.active {
  background-color: var(--fui-primary, #0078d4);
  border-color: var(--fui-primary, #0078d4);
}

.switch-slider.active::before {
  transform: translateX(24px);
  background-color: white;
}

.switch-label {
  font-size: 14px;
  font-weight: 500;
  color: var(--fui-text-secondary, #666);
  transition: color 0.3s;
  min-width: 30px;
}

.switch-label.active {
  color: var(--fui-primary, #0078d4);
  font-weight: 600;
}

/* 悬停效果 */
.switch-container:hover .switch-slider {
  border-color: rgba(255, 255, 255, 0.4);
}

.switch-container:hover .switch-slider.active {
  border-color: var(--fui-primary-hover, #106ebe);
}

/* 焦点效果 */
.switch-container input:focus + .switch-slider {
  box-shadow: 0 0 0 3px rgba(0, 120, 212, 0.2);
}
</style>