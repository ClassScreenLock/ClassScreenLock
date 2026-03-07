<template>
  <canvas ref="canvasRef" class="particles-canvas"></canvas>
</template>

<script setup>
import { ref, onMounted, onUnmounted } from 'vue'

const canvasRef = ref(null)
let ctx = null
let particles = []
let animationId = null
let mouse = { x: null, y: null, radius: 150 }

// 粒子配置
const config = {
  particleCount: 100,
  connectionDistance: 120,
  mouseRadius: 150,
  baseSpeed: 0.5,
  colors: [
    'rgba(0, 150, 255, 0.8)',
    'rgba(0, 200, 255, 0.8)',
    'rgba(100, 220, 255, 0.8)',
    'rgba(0, 180, 255, 0.8)',
    'rgba(255, 255, 255, 0.8)'
  ]
}

// 粒子类
class Particle {
  constructor() {
    this.x = Math.random() * canvasRef.value.width
    this.y = Math.random() * canvasRef.value.height
    this.size = Math.random() * 3 + 1
    this.baseX = this.x
    this.baseY = this.y
    this.density = (Math.random() * 30) + 1
    this.speed = Math.random() * config.baseSpeed + 0.2
    this.directionX = Math.random() * 2 - 1
    this.directionY = Math.random() * 2 - 1
    this.color = config.colors[Math.floor(Math.random() * config.colors.length)]
    this.angle = Math.random() * 360
    this.spin = Math.random() < 0.5 ? 1 : -1
  }

  draw() {
    ctx.beginPath()
    ctx.arc(this.x, this.y, this.size, 0, Math.PI * 2)
    ctx.fillStyle = this.color
    ctx.fill()
  }

  update() {
    // 鼠标互动
    let dx = mouse.x - this.x
    let dy = mouse.y - this.y
    let distance = Math.sqrt(dx * dx + dy * dy)
    
    if (distance < mouse.radius) {
      const forceDirectionX = dx / distance
      const forceDirectionY = dy / distance
      const force = (mouse.radius - distance) / mouse.radius
      const directionX = forceDirectionX * force * this.density
      const directionY = forceDirectionY * force * this.density
      
      this.x -= directionX
      this.y -= directionY
    } else {
      // 正常移动
      this.x += this.directionX * this.speed
      this.y += this.directionY * this.speed
      
      // 边界检测
      if (this.x < 0 || this.x > canvasRef.value.width) {
        this.directionX = -this.directionX
      }
      if (this.y < 0 || this.y > canvasRef.value.height) {
        this.directionY = -this.directionY
      }
    }
    
    // 自转动画
    this.angle += this.spin * 0.5
    
    this.draw()
  }
}

// 初始化画布
function initCanvas() {
  canvasRef.value.width = window.innerWidth
  canvasRef.value.height = window.innerHeight
  ctx = canvasRef.value.getContext('2d')
}

// 创建粒子
function createParticles() {
  particles = []
  for (let i = 0; i < config.particleCount; i++) {
    particles.push(new Particle())
  }
}

// 连接粒子
function connectParticles() {
  for (let a = 0; a < particles.length; a++) {
    for (let b = a + 1; b < particles.length; b++) {
      let dx = particles[a].x - particles[b].x
      let dy = particles[a].y - particles[b].y
      let distance = Math.sqrt(dx * dx + dy * dy)
      
      if (distance < config.connectionDistance) {
        let opacity = 1 - (distance / config.connectionDistance)
        ctx.strokeStyle = `rgba(100, 180, 255, ${opacity * 0.5})`
        ctx.lineWidth = 1
        ctx.beginPath()
        ctx.moveTo(particles[a].x, particles[a].y)
        ctx.lineTo(particles[b].x, particles[b].y)
        ctx.stroke()
      }
    }
  }
}

// 动画循环
function animate() {
  ctx.clearRect(0, 0, canvasRef.value.width, canvasRef.value.height)
  
  for (let i = 0; i < particles.length; i++) {
    particles[i].update()
  }
  
  connectParticles()
  animationId = requestAnimationFrame(animate)
}

// 鼠标移动事件
function handleMouseMove(event) {
  mouse.x = event.clientX
  mouse.y = event.clientY
}

// 鼠标离开事件
function handleMouseLeave() {
  mouse.x = null
  mouse.y = null
}

// 窗口大小变化
function handleResize() {
  canvasRef.value.width = window.innerWidth
  canvasRef.value.height = window.innerHeight
  createParticles()
}

onMounted(() => {
  initCanvas()
  createParticles()
  animate()
  
  window.addEventListener('mousemove', handleMouseMove)
  window.addEventListener('mouseleave', handleMouseLeave)
  window.addEventListener('resize', handleResize)
})

onUnmounted(() => {
  if (animationId) {
    cancelAnimationFrame(animationId)
  }
  window.removeEventListener('mousemove', handleMouseMove)
  window.removeEventListener('mouseleave', handleMouseLeave)
  window.removeEventListener('resize', handleResize)
})
</script>

<style scoped>
.particles-canvas {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  z-index: 0;
  pointer-events: none;
}
</style>
