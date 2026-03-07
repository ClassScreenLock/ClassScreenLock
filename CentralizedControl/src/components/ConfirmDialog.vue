<template>
  <Teleport to="body">
    <Transition name="confirm-overlay" appear>
      <div v-if="visible" class="confirm-overlay" @click="handleOverlayClick">
        <Transition name="confirm-dialog" appear>
          <div v-if="visible" class="confirm-dialog" @click.stop>
            <div class="confirm-header">
              <h3>{{ title }}</h3>
            </div>
            <div class="confirm-body">
              <p>{{ message }}</p>
            </div>
            <div class="confirm-footer">
              <button class="button-secondary" @click="handleCancel">取消</button>
              <button class="button-primary" @click="handleConfirm">确定</button>
            </div>
          </div>
        </Transition>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup>
import { ref, watch } from 'vue'

const props = defineProps({
  visible: {
    type: Boolean,
    default: false
  },
  title: {
    type: String,
    default: '确认操作'
  },
  message: {
    type: String,
    default: '您确定要执行此操作吗？'
  }
})

const emit = defineEmits(['update:visible', 'confirm', 'cancel'])

const handleConfirm = () => {
  emit('confirm')
  emit('update:visible', false)
}

const handleCancel = () => {
  emit('cancel')
  emit('update:visible', false)
}

const handleOverlayClick = () => {
  emit('cancel')
  emit('update:visible', false)
}
</script>

<style>
.confirm-overlay {
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

.confirm-overlay-enter-active,
.confirm-overlay-leave-active {
  transition: all 0.4s cubic-bezier(0.4, 0, 0.2, 1);
}

.confirm-overlay-enter-from,
.confirm-overlay-leave-to {
  opacity: 0;
}

.confirm-dialog {
  background: var(--fui-surface);
  border: 1px solid var(--fui-border);
  border-radius: var(--fui-radius);
  box-shadow: 0 20px 60px rgba(0, 0, 0, 0.3);
  max-width: 400px;
  width: 90%;
}

.confirm-dialog-enter-active {
  transition: all 0.4s cubic-bezier(0.34, 1.56, 0.64, 1);
}

.confirm-dialog-leave-active {
  transition: all 0.3s cubic-bezier(0.4, 0, 1, 1);
}

.confirm-dialog-enter-from {
  opacity: 0;
  transform: translateY(-30px) scale(0.9);
}

.confirm-dialog-leave-to {
  opacity: 0;
  transform: translateY(20px) scale(0.95);
}

.confirm-header {
  padding: 20px 24px;
  border-bottom: 1px solid var(--fui-border);
}

.confirm-header h3 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
  color: var(--fui-text);
}

.confirm-body {
  padding: 24px;
}

.confirm-body p {
  margin: 0;
  font-size: 14px;
  line-height: 1.5;
  color: var(--fui-text-secondary);
}

.confirm-footer {
  display: flex;
  gap: 12px;
  padding: 0 24px 24px;
  justify-content: flex-end;
}

.button-secondary {
  padding: 8px 16px;
  background: transparent;
  border: 1px solid var(--fui-border);
  border-radius: var(--fui-radius);
  color: var(--fui-text);
  font-size: 14px;
  cursor: pointer;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative;
  overflow: hidden;
}

.button-secondary::before {
  content: '';
  position: absolute;
  top: 50%;
  left: 50%;
  width: 0;
  height: 0;
  border-radius: 50%;
  background: var(--fui-primary);
  opacity: 0.1;
  transform: translate(-50%, -50%);
  transition: width 0.4s ease, height 0.4s ease;
}

.button-secondary:hover::before {
  width: 200%;
  height: 200%;
}

.button-secondary:hover {
  background: var(--fui-background);
  border-color: var(--fui-primary);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.1);
}

.button-secondary:active {
  transform: translateY(0);
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.1);
}

.button-primary {
  padding: 8px 16px;
  background: var(--fui-primary);
  border: none;
  border-radius: var(--fui-radius);
  color: white;
  font-size: 14px;
  cursor: pointer;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  position: relative;
  overflow: hidden;
}

.button-primary::before {
  content: '';
  position: absolute;
  top: 50%;
  left: 50%;
  width: 0;
  height: 0;
  border-radius: 50%;
  background: white;
  opacity: 0.2;
  transform: translate(-50%, -50%);
  transition: width 0.4s ease, height 0.4s ease;
}

.button-primary:hover::before {
  width: 200%;
  height: 200%;
}

.button-primary:hover {
  background: var(--fui-primary-hover);
  transform: translateY(-1px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2);
}

.button-primary:active {
  transform: translateY(0);
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.2);
}
</style>