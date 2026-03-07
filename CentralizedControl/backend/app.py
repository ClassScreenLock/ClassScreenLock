from flask import Flask, request, jsonify
from flask_cors import CORS
from datetime import datetime, timedelta
from database import Database
from functools import wraps
import sqlite3
import os
import json
import threading
import time
import hashlib

DB_PATH = os.path.join(os.path.dirname(__file__), 'classscreenlock.db')
db = Database(DB_PATH)

app = Flask(__name__)
CORS(app)

CONFIG_FILE = 'config.json'

# 记录服务器启动时间
SERVER_START_TIME = datetime.now()

# Token 有效期设置（48 小时）
TOKEN_EXPIRY_HOURS = 48

HEARTBEAT_TIMEOUT = 20

def check_device_heartbeat():
    with app.app_context():
        while True:
            try:
                devices = db.get_all_devices()
                now = datetime.now()
                
                for device in devices:
                    if device['status'] == 'online':
                        last_heartbeat = device.get('last_heartbeat')
                        if last_heartbeat:
                            try:
                                last_time = datetime.fromisoformat(last_heartbeat)
                                diff = (now - last_time).total_seconds()
                                
                                if diff > HEARTBEAT_TIMEOUT:
                                    db.update_device(
                                        device['id'],
                                        status='offline',
                                        offline_reason='heartbeat_timeout'
                                    )
                                    print(f"[HeartbeatChecker] 设备 {device['name']} 心跳超时，标记为离线")
                            except Exception as e:
                                print(f"[HeartbeatChecker] 解析心跳时间失败: {e}")
                        else:
                            db.update_device(
                                device['id'],
                                status='offline',
                                offline_reason='no_heartbeat'
                            )
            except Exception as e:
                print(f"[HeartbeatChecker] 检查心跳失败: {e}")
            
            time.sleep(10)

heartbeat_thread = threading.Thread(target=check_device_heartbeat, daemon=True)
heartbeat_thread.start()

def load_config():
    if os.path.exists(CONFIG_FILE):
        with open(CONFIG_FILE, 'r', encoding='utf-8') as f:
            return json.load(f)
    return {
        'backendPort': 5000,
        'serverPort': 5173,
        'systemName': 'ClassScreenLock 集控管理平台'
    }

def save_config(config):
    with open(CONFIG_FILE, 'w', encoding='utf-8') as f:
        json.dump(config, f, ensure_ascii=False, indent=2)

config = load_config()

def token_required(f):
    @wraps(f)
    def decorated(*args, **kwargs):
        token = request.headers.get('Authorization')
        if not token:
            return jsonify({'error': 'Token is missing'}), 401
        
        if token.startswith('Bearer '):
            token = token[7:]
        
        # 解析 token 格式：account_id:timestamp:hash
        try:
            parts = token.split(':')
            if len(parts) != 3:
                return jsonify({'error': 'Invalid token format'}), 401
            
            account_id = int(parts[0])
            timestamp = float(parts[1])
            token_hash = parts[2]
            
            # 验证 token 是否过期（48 小时）
            token_time = datetime.fromtimestamp(timestamp)
            if datetime.now() - token_time > timedelta(hours=TOKEN_EXPIRY_HOURS):
                return jsonify({'error': 'Token has expired, please login again'}), 401
            
            # 验证 token 哈希
            user = db.get_account_by_id(account_id)
            if not user:
                return jsonify({'error': 'User not found'}), 404
            
            # 重新计算哈希进行验证
            expected_hash = hashlib.sha256(
                f"{account_id}:{timestamp}:{user['password']}".encode()
            ).hexdigest()
            
            if token_hash != expected_hash:
                return jsonify({'error': 'Invalid token'}), 401
            
        except Exception as e:
            return jsonify({'error': f'Invalid token: {str(e)}'}), 401
        
        return f(user, *args, **kwargs)
    return decorated

@app.route('/api/auth/login', methods=['POST'])
def login():
    data = request.get_json()
    username = data.get('username')
    password = data.get('password')
    
    if not username or not password:
        return jsonify({'error': 'Username and password are required'}), 400
    
    if not db.verify_password(username, password):
        return jsonify({'error': 'Invalid username or password'}), 401
    
    account = db.get_account_by_username(username)
    db.update_last_login(account['id'])
    
    # 生成带时间戳的 token
    timestamp = datetime.now().timestamp()
    token_hash = hashlib.sha256(
        f"{account['id']}:{timestamp}:{account['password']}".encode()
    ).hexdigest()
    token = f"{account['id']}:{timestamp}:{token_hash}"
    
    return jsonify({
        'id': account['id'],
        'username': account['username'],
        'role': account['role'],
        'token': token,
        'expiresIn': TOKEN_EXPIRY_HOURS * 3600,  # 秒
        'expiresAt': (datetime.now() + timedelta(hours=TOKEN_EXPIRY_HOURS)).isoformat()
    })

@app.route('/api/auth/register', methods=['POST'])
def register():
    data = request.get_json()
    username = data.get('username')
    password = data.get('password')
    role = data.get('role', 'user')
    
    if not username or not password:
        return jsonify({'error': 'Username and password are required'}), 400
    
    if role not in ['super_admin', 'admin', 'user']:
        return jsonify({'error': 'Invalid role'}), 400
    
    try:
        account_id = db.create_account(username, password, role)
        account = db.get_account_by_id(account_id)
        return jsonify({
            'id': account['id'],
            'username': account['username'],
            'role': account['role']
        }), 201
    except sqlite3.IntegrityError:
        return jsonify({'error': 'Username already exists'}), 409

@app.route('/api/auth/logout', methods=['POST'])
@token_required
def logout(user):
    return jsonify({'message': 'Logged out successfully'})

@app.route('/api/auth/me', methods=['GET'])
@token_required
def get_current_user(user):
    return jsonify({
        'id': user['id'],
        'username': user['username'],
        'role': user['role'],
        'created_at': user['created_at'],
        'last_login_at': user['last_login_at']
    })

def format_account(account):
    """将账户数据转换为前端友好的格式"""
    return {
        'id': account['id'],
        'username': account['username'],
        'role': account['role'],
        'createdAt': account['created_at'],
        'lastLoginAt': account['last_login_at'],
        'isActive': bool(account['is_active'])
    }

@app.route('/api/accounts', methods=['GET'])
@token_required
def get_accounts(user):
    if user['role'] not in ['super_admin', 'admin']:
        return jsonify({'error': 'Permission denied'}), 403
    
    accounts = db.get_all_accounts()
    formatted_accounts = [format_account(account) for account in accounts]
    return jsonify(formatted_accounts)

@app.route('/api/accounts/<int:account_id>', methods=['GET'])
@token_required
def get_account(user, account_id):
    if user['role'] not in ['super_admin', 'admin']:
        return jsonify({'error': 'Permission denied'}), 403
    
    account = db.get_account_by_id(account_id)
    if not account:
        return jsonify({'error': 'Account not found'}), 404
    
    return jsonify(format_account(account))

@app.route('/api/accounts', methods=['POST'])
@token_required
def create_account(user):
    if user['role'] != 'super_admin':
        return jsonify({'error': 'Only super admin can create accounts'}), 403
    
    data = request.get_json()
    username = data.get('username')
    password = data.get('password')
    role = data.get('role', 'user')
    
    if not username or not password:
        return jsonify({'error': 'Username and password are required'}), 400
    
    if role not in ['super_admin', 'admin', 'user']:
        return jsonify({'error': 'Invalid role'}), 400
    
    try:
        account_id = db.create_account(username, password, role)
        account = db.get_account_by_id(account_id)
        return jsonify(format_account(account)), 201
    except sqlite3.IntegrityError:
        return jsonify({'error': 'Username already exists'}), 409

@app.route('/api/accounts/<int:account_id>', methods=['PUT'])
@token_required
def update_account(user, account_id):
    if user['role'] != 'super_admin':
        return jsonify({'error': 'Only super admin can update accounts'}), 403
    
    data = request.get_json()
    username = data.get('username')
    password = data.get('password')
    role = data.get('role')
    
    if role and role not in ['super_admin', 'admin', 'user']:
        return jsonify({'error': 'Invalid role'}), 400
    
    if not db.update_account(account_id, username=username, password=password, role=role):
        return jsonify({'error': 'Account not found or no changes made'}), 404
    
    account = db.get_account_by_id(account_id)
    return jsonify(format_account(account))

@app.route('/api/accounts/<int:account_id>', methods=['DELETE'])
@token_required
def delete_account(user, account_id):
    if user['role'] != 'super_admin':
        return jsonify({'error': 'Only super admin can delete accounts'}), 403
    
    if account_id == user['id']:
        return jsonify({'error': 'Cannot delete your own account'}), 400
    
    if not db.delete_account(account_id):
        return jsonify({'error': 'Account not found'}), 404
    
    return jsonify({'message': 'Account deleted successfully'})

def convert_organization_fields(organization):
    """将数据库字段转换为前端字段"""
    if not organization:
        return None
    
    converted = dict(organization)
    
    # 字段名映射
    field_mapping = {
        'province_code': 'provinceCode',
        'province_name': 'provinceName',
        'city_code': 'cityCode',
        'city_name': 'cityName',
        'district_code': 'districtCode',
        'district_name': 'districtName',
        'created_at': 'createdAt',
        'server_url': 'serverUrl'
    }
    
    for db_field, frontend_field in field_mapping.items():
        if db_field in converted:
            converted[frontend_field] = converted[db_field]
            del converted[db_field]
    
    return converted

@app.route('/api/organizations', methods=['GET'])
@token_required
def get_organizations(user):
    organizations = db.get_all_organizations()
    # 转换字段名
    converted_organizations = [convert_organization_fields(org) for org in organizations]
    return jsonify(converted_organizations)

@app.route('/api/organizations', methods=['POST'])
@token_required
def create_organization_endpoint(user):
    if user['role'] not in ['super_admin', 'admin']:
        return jsonify({'error': 'Permission denied'}), 403
    
    data = request.get_json()
    name = data.get('name')
    description = data.get('description')
    ip_address = data.get('ip_address')
    province_code = data.get('provinceCode') or data.get('province_code')
    city_code = data.get('cityCode') or data.get('city_code')
    district_code = data.get('districtCode') or data.get('district_code')
    server_url = data.get('server_url')
    
    if not name:
        return jsonify({'error': 'Name is required'}), 400
    
    org_id = db.create_organization(name, description, ip_address, province_code, city_code, district_code, server_url)
    organization = db.get_organization_by_id(org_id)
    # 转换字段名
    converted_organization = convert_organization_fields(organization)
    return jsonify(converted_organization), 201

@app.route('/api/organizations/<org_id>', methods=['GET'])
def get_organization(org_id):
    # 不需要认证，允许客户端查询组织信息以加入组织
    organization = db.get_organization_by_id(org_id)
    if not organization:
        return jsonify({'error': 'Organization not found'}), 404
    # 转换字段名
    converted_organization = convert_organization_fields(organization)
    return jsonify(converted_organization)

@app.route('/api/organizations/<org_id>', methods=['PUT'])
@token_required
def update_organization(user, org_id):
    if user['role'] not in ['super_admin', 'admin']:
        return jsonify({'error': 'Permission denied'}), 403
    
    data = request.get_json()
    name = data.get('name')
    description = data.get('description')
    
    if not db.update_organization(org_id, name=name, description=description):
        return jsonify({'error': 'Organization not found or no changes made'}), 404
    
    organization = db.get_organization_by_id(org_id)
    # 转换字段名
    converted_organization = convert_organization_fields(organization)
    return jsonify(converted_organization)

@app.route('/api/organizations/<org_id>', methods=['DELETE'])
@token_required
def delete_organization(user, org_id):
    if user['role'] not in ['super_admin', 'admin']:
        return jsonify({'error': 'Permission denied'}), 403
    
    if not db.delete_organization(org_id):
        return jsonify({'error': 'Organization not found'}), 404
    
    return jsonify({'message': 'Organization deleted successfully'})

def convert_device_fields(device):
    """将数据库字段转换为前端字段"""
    if not device:
        return None
    
    converted = dict(device)
    
    # 字段名映射
    field_mapping = {
        'last_heartbeat': 'lastHeartbeat',
        'last_seen': 'lastSeen',
        'exit_time': 'exitTime',
        'offline_reason': 'offlineReason',
        'contact_phone': 'contactPhone',
        'class_name': 'className',
        'person_in_charge': 'personInCharge',
        'cpu_usage': 'cpuUsage',
        'memory_usage': 'memoryUsage',
        'disk_usage': 'diskUsage',
        'os_version': 'osVersion',
        'app_version': 'appVersion',
        'dotnet_version': 'dotnetVersion',
        'organization_id': 'organizationId',
        'mac_address': 'macAddress',
        'ip_address': 'ipAddress',
        'registered_at': 'registeredAt'
    }
    
    for db_field, frontend_field in field_mapping.items():
        if db_field in converted:
            converted[frontend_field] = converted[db_field]
            del converted[db_field]
    
    return converted

@app.route('/api/devices', methods=['GET'])
@token_required
def get_devices(user):
    devices = db.get_all_devices()
    return jsonify([convert_device_fields(device) for device in devices])

@app.route('/api/devices/<path:device_id>', methods=['GET'])
@token_required
def get_device(user, device_id):
    device = db.get_device_by_id(device_id)
    if not device:
        return jsonify({'error': 'Device not found'}), 404
    
    return jsonify(convert_device_fields(device))

@app.route('/api/devices', methods=['POST'])
@token_required
def create_device_endpoint(user):
    if user['role'] not in ['super_admin', 'admin']:
        return jsonify({'error': 'Permission denied'}), 403
    
    data = request.get_json()
    name = data.get('name')
    ip_address = data.get('ipAddress') or data.get('ip_address')
    organization_id = data.get('organizationId') or data.get('organization_id')
    
    if not name or not ip_address:
        return jsonify({'error': 'Name and IP address are required'}), 400
    
    device_id = db.create_device(
        name=name,
        ip_address=ip_address,
        organization_id=organization_id,
        mac_address=data.get('macAddress') or data.get('mac_address'),
        os_version=data.get('osVersion') or data.get('os_version'),
        app_version=data.get('appVersion') or data.get('app_version'),
        dotnet_version=data.get('dotnetVersion') or data.get('dotnet_version'),
        contact_phone=data.get('contactPhone') or data.get('contact_phone'),
        class_name=data.get('className') or data.get('class_name'),
        person_in_charge=data.get('personInCharge') or data.get('person_in_charge')
    )
    device = db.get_device_by_id(device_id)
    return jsonify(convert_device_fields(device)), 201

@app.route('/api/devices/<path:device_id>', methods=['PUT'])
@token_required
def update_device(user, device_id):
    if user['role'] not in ['super_admin', 'admin']:
        return jsonify({'error': 'Permission denied'}), 403
    
    data = request.get_json()
    if not db.update_device(device_id, **data):
        return jsonify({'error': 'Device not found or no changes made'}), 404
    
    device = db.get_device_by_id(device_id)
    return jsonify(convert_device_fields(device))

@app.route('/api/devices/<path:device_id>', methods=['DELETE'])
@token_required
def delete_device(user, device_id):
    if user['role'] not in ['super_admin', 'admin']:
        return jsonify({'error': 'Permission denied'}), 403
    
    if not db.delete_device(device_id):
        return jsonify({'error': 'Device not found'}), 404
    
    return jsonify({'message': 'Device deleted successfully'})

@app.route('/api/devices/<path:device_id>/heartbeat', methods=['POST'])
def device_heartbeat(device_id):
    data = request.get_json()
    
    now = datetime.now().isoformat()
    
    heartbeat_data = {
        'status': data.get('status', 'online'),
        'cpu_usage': data.get('cpuUsage', 0),
        'memory_usage': data.get('memoryUsage', 0),
        'disk_usage': data.get('diskUsage', 0),
        'last_heartbeat': now,
        'last_seen': now
    }
    
    if data.get('status') == 'offline':
        heartbeat_data['offline_reason'] = data.get('reason', 'unknown')
        heartbeat_data['exit_time'] = now
    else:
        # 设备上线时，清除离线原因和退出时间
        heartbeat_data['offline_reason'] = None
        heartbeat_data['exit_time'] = None
    
    if db.update_device(device_id, **heartbeat_data):
        return jsonify({'message': 'Heartbeat received', 'device_id': device_id})
    else:
        return jsonify({'error': 'Device not found'}), 404

@app.route('/api/devices/<path:device_id>/logout', methods=['POST'])
def device_logout(device_id):
    data = request.get_json()
    
    logout_data = {
        'status': 'logged_out',
        'exit_time': datetime.now().isoformat(),
        'offline_reason': data.get('exitReason', 'user_logout')
    }
    
    if db.update_device(device_id, **logout_data):
        return jsonify({'message': 'Device logged out', 'device_id': device_id})
    else:
        return jsonify({'error': 'Device not found'}), 404

@app.route('/api/organizations/<org_id>/devices', methods=['GET'])
@token_required
def get_organization_devices(user, org_id):
    devices = db.get_devices_by_organization(org_id)
    return jsonify([convert_device_fields(device) for device in devices])

@app.route('/api/organizations/<org_id>/devices', methods=['POST'])
def add_device_to_organization(org_id):
    try:
        print(f"[DEBUG] 收到设备注册请求，组织ID: {org_id}")
        organization = db.get_organization_by_id(org_id)
        if not organization:
            print(f"[DEBUG] 组织不存在: {org_id}")
            return jsonify({'error': 'Organization not found'}), 404
        
        data = request.get_json()
        print(f"[DEBUG] 接收到的数据: {data}")
        
        name = data.get('name')
        ip_address = data.get('ipAddress') or data.get('ip_address')
        mac_address = data.get('macAddress') or data.get('mac_address')
        os_version = data.get('osVersion') or data.get('os_version')
        app_version = data.get('appVersion') or data.get('app_version')
        dotnet_version = data.get('dotnetVersion') or data.get('dotnet_version')
        contact_phone = data.get('contactPhone') or data.get('contact_phone')
        class_name = data.get('className') or data.get('class_name')
        person_in_charge = data.get('personInCharge') or data.get('person_in_charge')
        device_id = data.get('id')
        
        if not name:
            print(f"[DEBUG] 设备名称为空")
            return jsonify({'error': 'Device name is required'}), 400
        
        now = datetime.now().isoformat()
        print(f"[DEBUG] 开始创建设备: {name}")
        
        created_device_id = db.create_device(
            name=name,
            ip_address=ip_address or '',
            organization_id=org_id,
            device_id=device_id,
            mac_address=mac_address,
            os_version=os_version,
            app_version=app_version,
            dotnet_version=dotnet_version,
            contact_phone=contact_phone,
            class_name=class_name,
            person_in_charge=person_in_charge
        )
        
        print(f"[DEBUG] 设备创建成功，ID: {created_device_id}")
        
        db.update_device(created_device_id, status='online', last_heartbeat=now, last_seen=now)
        
        device = db.get_device_by_id(created_device_id)
        return jsonify(convert_device_fields(device)), 201
    except Exception as e:
        print(f"[ERROR] 设备注册失败: {str(e)}")
        import traceback
        traceback.print_exc()
        return jsonify({'error': str(e)}), 500

@app.route('/api/devices/<path:device_id>/network-rules', methods=['GET'])
def get_device_network_rules(device_id):
    """获取设备所属组织的网络拦截规则"""
    device = db.get_device_by_id(device_id)
    if not device:
        return jsonify({'error': 'Device not found'}), 404
    
    org_id = device.get('organization_id')
    if not org_id:
        return jsonify({'domainRules': []})
    
    # 获取组织的网络配置
    config = db.get_organization_config(org_id)
    if config and config.get('network_config'):
        network_config = config['network_config']
        return jsonify({
            'enabled': network_config.get('enabled', True),
            'mode': network_config.get('mode', 'blacklist'),
            'domainRules': network_config.get('domainRules', [])
        })
    
    # 返回默认空规则
    return jsonify({
        'enabled': True,
        'mode': 'blacklist',
        'domainRules': []
    })

@app.route('/api/organizations/<org_id>/security-config', methods=['GET', 'PUT'])
def security_config(org_id):
    if request.method == 'GET':
        # 从数据库获取配置
        config = db.get_organization_config(org_id)
        if config and config.get('security_config'):
            return jsonify(config['security_config'])
        # 返回默认安全配置
        return jsonify({
            'admin': {
                'adminUsername': 'admin',
                'password': ''
            },
            'accounts': [],
            'security': {
                'isTwoFactorEnabled': False,
                'twoFactorSecret': '',
                'loginVerificationMode': 0
            },
            'permissions': {
                'exitAppMinAccountType': 0,
                'sidebarHomeMinAccountType': 0,
                'sidebarLockSettingsMinAccountType': 0,
                'breakTimeLockSettingsMinAccountType': 0,
                'sidebarScheduleMinAccountType': 0,
                'sidebarAppManagementMinAccountType': 0,
                'sidebarNetworkInterceptionMinAccountType': 0,
                'sidebarSecurityLogsMinAccountType': 0,
                'sidebarScreenshotHistoryMinAccountType': 0,
                'sidebarWebcamHistoryMinAccountType': 0,
                'sidebarAutomationMinAccountType': 0,
                'sidebarSecurityCenterMinAccountType': 0,
                'sidebarSettingsMinAccountType': 0,
                'sidebarAboutMinAccountType': 0,
                'earlyUnlockMinAccountType': 2
            },
            'syncInterval': 30
        })
    else:
        # 保存安全配置
        data = request.get_json()
        # 获取现有配置
        config = db.get_organization_config(org_id)
        network_config = config.get('network_config') if config else None
        # 保存新配置
        db.save_organization_config(org_id, security_config=data, network_config=network_config)
        return jsonify(data)

@app.route('/api/organizations/<org_id>/network-config', methods=['GET', 'PUT'])
def network_config(org_id):
    if request.method == 'GET':
        # 获取组织的网络配置
        config = db.get_organization_config(org_id)
        if config and config.get('network_config'):
            network_config = config['network_config']
            return jsonify({
                'enabled': network_config.get('enabled', True),
                'mode': network_config.get('mode', 'blacklist'),
                'domainRules': network_config.get('domainRules', [])
            })
        
        # 返回空配置（不返回默认规则，让用户自己配置）
        return jsonify({
            'enabled': True,
            'mode': 'blacklist',
            'domainRules': []
        })
    else:
        # 保存网络配置
        data = request.get_json()
        # 获取现有配置
        config = db.get_organization_config(org_id)
        security_config = config.get('security_config') if config else None
        # 保存新配置
        db.save_organization_config(org_id, security_config=security_config, network_config=data)
        return jsonify(data)

@app.route('/api/migrate', methods=['POST'])
def migrate_data():
    import os
    json_dir = os.path.join(os.path.dirname(__file__), '..', 'data')
    result = db.migrate_from_json(json_dir)
    return jsonify(result)

@app.route('/api/health', methods=['GET'])
def health_check():
    return jsonify({'status': 'healthy', 'timestamp': datetime.now().isoformat()})

@app.route('/api/uptime', methods=['GET'])
def get_uptime():
    """获取服务器运行时间"""
    now = datetime.now()
    diff = (now - SERVER_START_TIME).total_seconds()
    
    days = int(diff // (24 * 3600))
    hours = int((diff % (24 * 3600)) // 3600)
    minutes = int((diff % 3600) // 60)
    seconds = int(diff % 60)
    
    return jsonify({
        'uptime': f'{days}天 {hours}小时 {minutes}分钟 {seconds}秒',
        'start_time': SERVER_START_TIME.isoformat(),
        'days': days,
        'hours': hours,
        'minutes': minutes,
        'seconds': seconds
    })

@app.route('/api/config', methods=['GET'])
def get_config():
    return jsonify(config)

@app.route('/api/config', methods=['POST'])
def update_config():
    data = request.get_json()
    config.update(data)
    save_config(config)
    return jsonify({'message': 'Config updated', 'config': config})

if __name__ == '__main__':
    backend_port = config.get('backendPort', 5000)
    app.run(debug=True, host='0.0.0.0', port=backend_port)
