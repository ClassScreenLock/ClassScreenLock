import axios from 'axios'

const getBackendURL = () => {
  const config = localStorage.getItem('systemSettings')
  if (config) {
    const settings = JSON.parse(config)
    const port = settings.serverPort || 5173
    return `http://localhost:${port}/api`
  }
  return 'http://localhost:5173/api'
}

const api = axios.create({
  baseURL: getBackendURL(),
  timeout: 5000,
  headers: {
    'Content-Type': 'application/json'
  }
})

api.interceptors.request.use(
  config => {
    config.baseURL = getBackendURL()
    
    const user = localStorage.getItem('user')
    if (user) {
      const userData = JSON.parse(user)
      if (userData.token) {
        config.headers = config.headers || {}
        config.headers['Authorization'] = `Bearer ${userData.token}`
      }
    }
    
    return config
  },
  error => {
    return Promise.reject(error)
  }
)

api.interceptors.response.use(
  response => {
    return response.data
  },
  error => {
    console.error('API Error:', error)
    return Promise.reject(error)
  }
)

export const configAPI = {
  get: () => api.get('/config'),
  update: (data) => api.post('/config', data)
}

export const systemAPI = {
  getUptime: () => api.get('/uptime')
}

export default api
