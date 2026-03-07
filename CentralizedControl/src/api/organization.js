import api from './index'

export const organizationAPI = {
  getAll: () => api.get('/organizations'),
  getById: (id) => api.get(`/organizations/${id}`),
  create: (data) => api.post('/organizations', data),
  update: (id, data) => api.put(`/organizations/${id}`, data),
  delete: (id) => api.delete(`/organizations/${id}`),
  getDevices: (id) => api.get(`/organizations/${id}/devices`),
  getSecurityConfig: (id) => api.get(`/organizations/${id}/security-config`),
  updateSecurityConfig: (id, config) => api.put(`/organizations/${id}/security-config`, config),
  getNetworkConfig: (id) => api.get(`/organizations/${id}/network-config`),
  updateNetworkConfig: (id, config) => api.put(`/organizations/${id}/network-config`, config)
}

export const deviceAPI = {
  getAll: () => api.get('/devices'),
  getById: (id) => api.get(`/devices/${id}`),
  updateStatus: (id, status) => api.patch(`/devices/${id}/status`, { status }),
  delete: (id) => api.delete(`/devices/${id}`)
}
