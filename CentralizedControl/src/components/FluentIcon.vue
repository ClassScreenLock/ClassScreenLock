<template>
  <svg
    :viewBox="viewBox"
    :width="size"
    :height="size"
    :fill="fill"
    xmlns="http://www.w3.org/2000/svg"
    class="fluent-icon"
    :class="[`fluent-icon-${name}`, className]"
  >
    <slot />
  </svg>
</template>

<script setup>
import { computed } from 'vue'

const props = defineProps({
  name: {
    type: String,
    required: true
  },
  size: {
    type: [Number, String],
    default: 20
  },
  fill: {
    type: String,
    default: 'currentColor'
  },
  viewBox: {
    type: String,
    default: '0 0 24 24'
  },
  className: {
    type: String,
    default: ''
  }
})

const className = computed(() => props.className)
</script>

<style scoped>
.fluent-icon {
  display: inline-block;
  vertical-align: middle;
  flex-shrink: 0;
  transition: all 0.2s cubic-bezier(0.4, 0, 0.2, 1);
}

.fluent-icon:hover {
  transform: scale(1.05);
}
</style>
