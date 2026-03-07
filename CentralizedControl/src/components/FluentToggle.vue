<template>
  <div 
    class="fluent-toggle"
    :class="{ 'checked': checked, 'disabled': disabled }"
    @click="handleToggle"
  >
    <div class="toggle-track">
      <div class="toggle-thumb"></div>
    </div>
  </div>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  checked: {
    type: Boolean,
    default: false
  },
  disabled: {
    type: Boolean,
    default: false
  }
})

const emit = defineEmits(['update:checked'])

const handleToggle = () => {
  if (!props.disabled) {
    emit('update:checked', !props.checked)
  }
}
</script>

<style scoped>
.fluent-toggle {
  position: relative;
  display: inline-block;
  width: 44px;
  height: 24px;
  cursor: pointer;
  user-select: none;
}

.fluent-toggle.disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.toggle-track {
  position: absolute;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: #E1E1E1;
  border-radius: 12px;
  transition: all 0.2s ease-in-out;
  border: 1px solid #E1E1E1;
}

.toggle-thumb {
  position: absolute;
  top: 1px;
  left: 1px;
  width: 20px;
  height: 20px;
  background-color: white;
  border-radius: 50%;
  transition: all 0.2s ease-in-out;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.1);
}

.fluent-toggle.checked .toggle-track {
  background-color: #0078D4;
  border-color: #0078D4;
}

.fluent-toggle.checked .toggle-thumb {
  transform: translateX(20px);
  box-shadow: 0 1px 3px rgba(0, 120, 212, 0.3);
}

.fluent-toggle:hover:not(.disabled) .toggle-track {
  background-color: #D1D1D1;
}

.fluent-toggle.checked:hover:not(.disabled) .toggle-track {
  background-color: #006BB4;
}

.fluent-toggle:active:not(.disabled) .toggle-thumb {
  transform: scale(0.95);
}

.fluent-toggle.checked:active:not(.disabled) .toggle-thumb {
  transform: translateX(20px) scale(0.95);
}
</style>