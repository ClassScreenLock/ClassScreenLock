import sqlite3
import bcrypt
from datetime import datetime
from typing import List, Optional, Dict, Any

class Database:
    def __init__(self, db_path: str = 'classscreenlock.db'):
        self.db_path = db_path
        self.region_code_map = self._init_region_code_map()
        self.init_database()
    
    def _init_region_code_map(self):
        return {
            '1100': '北京市',
            '1200': '天津市',
            '1300': '河北省',
            '1400': '山西省',
            '1500': '内蒙古自治区',
            '2100': '辽宁省',
            '2200': '吉林省',
            '2300': '黑龙江省',
            '3100': '上海市',
            '3200': '江苏省',
            '3300': '浙江省',
            '3400': '安徽省',
            '3500': '福建省',
            '3600': '江西省',
            '3700': '山东省',
            '4100': '河南省',
            '4200': '湖北省',
            '4300': '湖南省',
            '4400': '广东省',
            '4500': '广西壮族自治区',
            '4600': '海南省',
            '5000': '重庆市',
            '5100': '四川省',
            '5200': '贵州省',
            '5300': '云南省',
            '5400': '西藏自治区',
            '6100': '陕西省',
            '6200': '甘肃省',
            '6300': '青海省',
            '6400': '宁夏回族自治区',
            '6500': '新疆维吾尔自治区',
            '7100': '台湾省',
            '8100': '香港特别行政区',
            '8200': '澳门特别行政区'
        }
    
    def _get_region_code_from_ip(self, ip_address: str) -> str:
        if not ip_address:
            return '0000'
        
        try:
            ip_parts = ip_address.split('.')
            if len(ip_parts) != 4:
                return '0000'
            
            first_octet = int(ip_parts[0])
            
            if first_octet >= 1 and first_octet <= 126:
                return '1100'
            elif first_octet >= 128 and first_octet <= 191:
                return '4400'
            elif first_octet >= 192 and first_octet <= 223:
                return '3100'
            else:
                return '0000'
        except:
            return '0000'
    
    def _generate_checksum(self, data: str) -> str:
        import hashlib
        hash_obj = hashlib.md5(data.encode('utf-8'))
        hash_hex = hash_obj.hexdigest()
        # 将十六进制哈希转换为十进制数字，然后取前6位数字作为校验和
        checksum = int(hash_hex[:8], 16)
        # 确保校验和为6位数字，不足则补零
        return f'{checksum % 1000000:06d}'
    
    def _generate_organization_id(self, ip_address: str = None, province_code: str = None) -> str:
        import hashlib
        import socket
        now = datetime.now()
        date_part = now.strftime('%Y%m%d')  # 8位日期
        
        with self.get_connection() as conn:
            cursor = conn.cursor()
            today_start = now.replace(hour=0, minute=0, second=0, microsecond=0).isoformat()
            cursor.execute('''
                SELECT COUNT(*) as count FROM organizations 
                WHERE created_at >= ?
            ''', (today_start,))
            count = cursor.fetchone()[0] + 1
            sequence_part = f'{count:06d}'  # 6位序列号
        
        # 使用6位地区代码
        if province_code and len(province_code) == 6:
            region_code = province_code
        else:
            # 固定使用000000作为默认地区代码
            region_code = '000000'  # 6位地区代码
        
        # 使用服务器标识的数字表示
        try:
            hostname = socket.gethostname()
            # 将主机名转换为数字
            hostname_hash = hashlib.md5(hostname.encode('utf-8')).hexdigest()
            server_id = str(int(hostname_hash[:8], 16))[:4].ljust(4, '0')  # 4位服务器标识
        except:
            server_id = '0000'  # 4位默认服务器标识
        
        # 计算校验和
        base_id = f'{date_part}{sequence_part}{region_code}{server_id}'
        checksum = self._generate_checksum(base_id)
        
        # 组合最终ID，确保总长不超过32位
        final_id = f'{date_part}{sequence_part}{region_code}{server_id}{checksum}'
        
        # 确保ID长度不超过32位
        if len(final_id) > 32:
            # 截断各部分以确保长度不超过32位
            total_length = len(final_id)
            excess = total_length - 32
            
            # 优先减少服务器标识部分
            if excess > 0:
                server_id = server_id[:-excess].ljust(len(server_id) - excess, '0')
                final_id = f'{date_part}{sequence_part}{region_code}{server_id}{checksum}'
        
        return final_id
    
    def get_connection(self):
        conn = sqlite3.connect(self.db_path)
        conn.row_factory = sqlite3.Row
        return conn
    
    def init_database(self):
        with self.get_connection() as conn:
            cursor = conn.cursor()
            
            cursor.execute('''
                CREATE TABLE IF NOT EXISTS accounts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    username TEXT UNIQUE NOT NULL,
                    password TEXT NOT NULL,
                    role TEXT NOT NULL,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    last_login_at TIMESTAMP,
                    is_active BOOLEAN DEFAULT 1
                )
            ''')
            
            cursor.execute('''
                CREATE TABLE IF NOT EXISTS organizations (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    description TEXT,
                    server_url TEXT,
                    province_code TEXT,
                    province_name TEXT,
                    city_code TEXT,
                    city_name TEXT,
                    district_code TEXT,
                    district_name TEXT,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )
            ''')
            
            cursor.execute('''
                CREATE TABLE IF NOT EXISTS organization_configs (
                    id TEXT PRIMARY KEY,
                    organization_id TEXT NOT NULL,
                    security_config TEXT,
                    network_config TEXT,
                    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (organization_id) REFERENCES organizations(id)
                )
            ''')
            
            cursor.execute('''
                CREATE TABLE IF NOT EXISTS devices (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    ip_address TEXT NOT NULL,
                    mac_address TEXT,
                    organization_id TEXT,
                    status TEXT DEFAULT 'offline',
                    os_version TEXT,
                    app_version TEXT,
                    dotnet_version TEXT,
                    registered_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                    last_heartbeat TIMESTAMP,
                    last_seen TIMESTAMP,
                    exit_time TIMESTAMP,
                    offline_reason TEXT,
                    contact_phone TEXT,
                    class_name TEXT,
                    person_in_charge TEXT,
                    cpu_usage REAL,
                    memory_usage REAL,
                    disk_usage REAL,
                    FOREIGN KEY (organization_id) REFERENCES organizations(id)
                )
            ''')
            
            conn.commit()
            
            if not cursor.execute('SELECT COUNT(*) FROM accounts').fetchone()[0]:
                self._create_default_admin(conn)
    
    def _create_default_admin(self, conn):
        hashed_password = bcrypt.hashpw('admin123'.encode('utf-8'), bcrypt.gensalt()).decode('utf-8')
        cursor = conn.cursor()
        cursor.execute('''
            INSERT INTO accounts (username, password, role)
            VALUES (?, ?, ?)
        ''', ('admin', hashed_password, 'super_admin'))
        conn.commit()
    
    def create_account(self, username: str, password: str, role: str) -> int:
        hashed_password = bcrypt.hashpw(password.encode('utf-8'), bcrypt.gensalt()).decode('utf-8')
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('''
                INSERT INTO accounts (username, password, role)
                VALUES (?, ?, ?)
            ''', (username, hashed_password, role))
            conn.commit()
            return cursor.lastrowid
    
    def get_all_accounts(self) -> List[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('SELECT * FROM accounts WHERE is_active = 1')
            return [dict(row) for row in cursor.fetchall()]
    
    def get_account_by_id(self, account_id: int) -> Optional[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('SELECT * FROM accounts WHERE id = ? AND is_active = 1', (account_id,))
            row = cursor.fetchone()
            return dict(row) if row else None
    
    def get_account_by_username(self, username: str) -> Optional[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('SELECT * FROM accounts WHERE username = ? AND is_active = 1', (username,))
            row = cursor.fetchone()
            return dict(row) if row else None
    
    def update_account(self, account_id: int, username: Optional[str] = None, 
                       password: Optional[str] = None, role: Optional[str] = None) -> bool:
        updates = []
        params = []
        
        if username:
            updates.append('username = ?')
            params.append(username)
        if password:
            hashed_password = bcrypt.hashpw(password.encode('utf-8'), bcrypt.gensalt()).decode('utf-8')
            updates.append('password = ?')
            params.append(hashed_password)
        if role:
            updates.append('role = ?')
            params.append(role)
        
        if not updates:
            return False
        
        params.append(account_id)
        
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(f'UPDATE accounts SET {", ".join(updates)} WHERE id = ?', params)
            conn.commit()
            return cursor.rowcount > 0
    
    def delete_account(self, account_id: int) -> bool:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('UPDATE accounts SET is_active = 0 WHERE id = ?', (account_id,))
            conn.commit()
            return cursor.rowcount > 0
    
    def verify_password(self, username: str, password: str) -> bool:
        account = self.get_account_by_username(username)
        if not account:
            return False
        stored_hash = account['password']
        if isinstance(stored_hash, str):
            stored_hash = stored_hash.encode('utf-8')
        return bcrypt.checkpw(password.encode('utf-8'), stored_hash)
    
    def update_last_login(self, account_id: int):
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('UPDATE accounts SET last_login_at = ? WHERE id = ?', 
                         (datetime.now().isoformat(), account_id))
            conn.commit()
    
    def create_organization(self, name: str, description: str = None, ip_address: str = None, 
                           province_code: str = None, city_code: str = None, district_code: str = None, 
                           server_url: str = None) -> str:
        # 使用最详细的地区代码生成组织ID
        region_code = province_code
        if city_code:
            region_code = city_code
        if district_code:
            region_code = district_code
        
        org_id = self._generate_organization_id(ip_address, region_code)
        
        # 获取地区名称
        province_name = None
        city_name = None
        district_name = None
        
        # 构建地区代码映射
        province_map = {
            '110000': '北京市', '120000': '天津市', '130000': '河北省', '140000': '山西省',
            '150000': '内蒙古自治区', '210000': '辽宁省', '220000': '吉林省', '230000': '黑龙江省',
            '310000': '上海市', '320000': '江苏省', '330000': '浙江省', '340000': '安徽省',
            '350000': '福建省', '360000': '江西省', '370000': '山东省', '410000': '河南省',
            '420000': '湖北省', '430000': '湖南省', '440000': '广东省', '450000': '广西壮族自治区',
            '460000': '海南省', '500000': '重庆市', '510000': '四川省', '520000': '贵州省',
            '530000': '云南省', '540000': '西藏自治区', '610000': '陕西省', '620000': '甘肃省',
            '630000': '青海省', '640000': '宁夏回族自治区', '650000': '新疆维吾尔自治区'
        }
        
        city_map = {
            # 北京市
            '110100': '市辖区',
            # 天津市
            '120100': '市辖区',
            # 河北省
            '130100': '石家庄市', '130200': '唐山市', '130300': '秦皇岛市', '130400': '邯郸市',
            '130500': '邢台市', '130600': '保定市', '130700': '张家口市', '130800': '承德市',
            '130900': '沧州市', '131000': '廊坊市', '131100': '衡水市',
            # 山西省
            '140100': '太原市', '140200': '大同市', '140300': '阳泉市', '140400': '长治市',
            '140500': '晋城市', '140600': '朔州市', '140700': '晋中市', '140800': '运城市',
            '140900': '忻州市', '141000': '临汾市', '141100': '吕梁市',
            # 上海市
            '310100': '市辖区',
            # 江苏省
            '320100': '南京市', '320200': '无锡市', '320300': '徐州市', '320400': '常州市',
            '320500': '苏州市', '320600': '南通市', '320700': '连云港市', '320800': '淮安市',
            '320900': '盐城市', '321000': '扬州市', '321100': '镇江市', '321200': '泰州市',
            '321300': '宿迁市',
            # 浙江省
            '330100': '杭州市', '330200': '宁波市', '330300': '温州市', '330400': '嘉兴市',
            '330500': '湖州市', '330600': '绍兴市', '330700': '金华市', '330800': '衢州市',
            '330900': '舟山市', '331000': '台州市', '331100': '丽水市',
            # 福建省
            '350100': '福州市', '350200': '厦门市', '350300': '莆田市', '350400': '三明市',
            '350500': '泉州市', '350600': '漳州市', '350700': '南平市', '350800': '龙岩市',
            '350900': '宁德市',
            # 广东省
            '440100': '广州市', '440200': '韶关市', '440300': '深圳市', '440400': '珠海市',
            '440500': '汕头市', '440600': '佛山市', '440700': '江门市', '440800': '湛江市',
            '440900': '茂名市', '441200': '肇庆市', '441300': '惠州市', '441400': '梅州市',
            '441500': '汕尾市', '441600': '河源市', '441700': '阳江市', '441800': '清远市',
            '441900': '东莞市', '442000': '中山市', '445100': '潮州市', '445200': '揭阳市',
            '445300': '云浮市'
        }
        
        # 区县名称映射
        district_map = {
            # 北京市市辖区
            '110101': '东城区', '110102': '西城区', '110105': '朝阳区', '110106': '丰台区',
            '110107': '石景山区', '110108': '海淀区', '110109': '门头沟区', '110111': '房山区',
            '110112': '通州区', '110113': '顺义区', '110114': '昌平区', '110115': '大兴区',
            '110116': '怀柔区', '110117': '平谷区', '110118': '密云区', '110119': '延庆区',
            # 上海市市辖区
            '310101': '黄浦区', '310104': '徐汇区', '310105': '长宁区', '310106': '静安区',
            '310107': '普陀区', '310109': '虹口区', '310110': '杨浦区', '310112': '闵行区',
            '310113': '宝山区', '310114': '嘉定区', '310115': '浦东新区', '310116': '金山区',
            '310117': '松江区', '310118': '青浦区', '310119': '奉贤区', '310151': '崇明区',
            # 广州市
            '440103': '荔湾区', '440104': '越秀区', '440105': '海珠区', '440106': '天河区',
            '440111': '白云区', '440112': '黄埔区', '440113': '番禺区', '440114': '花都区',
            '440115': '南沙区', '440116': '从化区', '440117': '增城区',
            # 深圳市
            '440303': '罗湖区', '440304': '福田区', '440305': '南山区', '440306': '宝安区',
            '440307': '龙岗区', '440308': '盐田区', '440309': '龙华区', '440310': '坪山区',
            '440311': '光明区', '440312': '大鹏新区'
        }
        
        # 从映射中获取地区名称
        if province_code and province_code in province_map:
            province_name = province_map[province_code]
        
        if city_code and city_code in city_map:
            city_name = city_map[city_code]
        
        if district_code and district_code in district_map:
            district_name = district_map[district_code]
        
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('''
                INSERT INTO organizations (id, name, description, server_url, province_code, province_name, 
                                         city_code, city_name, district_code, district_name)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            ''', (org_id, name, description, server_url, province_code, province_name, 
                  city_code, city_name, district_code, district_name))
            conn.commit()
            return org_id
    
    def get_all_organizations(self) -> List[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('SELECT * FROM organizations')
            return [dict(row) for row in cursor.fetchall()]
    
    def get_organization_by_id(self, org_id: str) -> Optional[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            print(f"[DEBUG] 查询组织 ID: {org_id} (类型: {type(org_id)})")
            cursor.execute('SELECT * FROM organizations WHERE id = ?', (org_id,))
            row = cursor.fetchone()
            print(f"[DEBUG] 查询结果: {row}")
            return dict(row) if row else None
    
    def update_organization(self, org_id: str, name: Optional[str] = None, 
                           description: Optional[str] = None) -> bool:
        updates = []
        params = []
        
        if name:
            updates.append('name = ?')
            params.append(name)
        if description is not None:
            updates.append('description = ?')
            params.append(description)
        
        if not updates:
            return False
        
        params.append(org_id)
        
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(f'UPDATE organizations SET {", ".join(updates)} WHERE id = ?', params)
            conn.commit()
            return cursor.rowcount > 0
    
    def delete_organization(self, org_id: str) -> bool:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            # 先删除组织
            cursor.execute('DELETE FROM organizations WHERE id = ?', (org_id,))
            org_deleted = cursor.rowcount > 0
            # 再删除相关配置
            cursor.execute('DELETE FROM organization_configs WHERE organization_id = ?', (org_id,))
            conn.commit()
            return org_deleted
    
    def get_organization_config(self, org_id: str) -> Optional[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('SELECT * FROM organization_configs WHERE organization_id = ?', (org_id,))
            row = cursor.fetchone()
            if row:
                config = dict(row)
                if config.get('security_config'):
                    try:
                        import json
                        config['security_config'] = json.loads(config['security_config'])
                    except:
                        pass
                if config.get('network_config'):
                    try:
                        import json
                        config['network_config'] = json.loads(config['network_config'])
                    except:
                        pass
                return config
            return None
    
    def save_organization_config(self, org_id: str, security_config: Dict = None, 
                                 network_config: Dict = None) -> bool:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            import json
            security_json = json.dumps(security_config) if security_config else None
            network_json = json.dumps(network_config) if network_config else None
            
            cursor.execute('''
                INSERT OR REPLACE INTO organization_configs 
                (id, organization_id, security_config, network_config, updated_at)
                VALUES (?, ?, ?, ?, ?)
            ''', (org_id, org_id, security_json, network_json, datetime.now().isoformat()))
            conn.commit()
            return True
    
    def create_device(self, name: str, ip_address: str, organization_id: str = None, 
                     device_id: str = None, **kwargs) -> str:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            if device_id:
                # 检查设备是否已存在
                cursor.execute('SELECT id FROM devices WHERE id = ?', (device_id,))
                existing = cursor.fetchone()
                if existing:
                    # 设备已存在，更新信息而不是插入新记录
                    cursor.execute('''
                        UPDATE devices SET 
                            name = ?, ip_address = ?, organization_id = ?, 
                            mac_address = ?, os_version = ?, app_version = ?, 
                            dotnet_version = ?, contact_phone = ?, class_name = ?, 
                            person_in_charge = ?
                        WHERE id = ?
                    ''', (name, ip_address, organization_id, kwargs.get('mac_address'),
                         kwargs.get('os_version'), kwargs.get('app_version'), 
                         kwargs.get('dotnet_version'), kwargs.get('contact_phone'),
                         kwargs.get('class_name'), kwargs.get('person_in_charge'), device_id))
                    conn.commit()
                    return device_id
                
                cursor.execute('''
                    INSERT INTO devices (id, name, ip_address, organization_id, mac_address, 
                                        os_version, app_version, dotnet_version, 
                                        contact_phone, class_name, person_in_charge)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (device_id, name, ip_address, organization_id, kwargs.get('mac_address'),
                     kwargs.get('os_version'), kwargs.get('app_version'), 
                     kwargs.get('dotnet_version'), kwargs.get('contact_phone'),
                     kwargs.get('class_name'), kwargs.get('person_in_charge')))
                conn.commit()
                return device_id
            else:
                cursor.execute('''
                    INSERT INTO devices (name, ip_address, organization_id, mac_address, 
                                        os_version, app_version, dotnet_version, 
                                        contact_phone, class_name, person_in_charge)
                    VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                ''', (name, ip_address, organization_id, kwargs.get('mac_address'),
                     kwargs.get('os_version'), kwargs.get('app_version'), 
                     kwargs.get('dotnet_version'), kwargs.get('contact_phone'),
                     kwargs.get('class_name'), kwargs.get('person_in_charge')))
                conn.commit()
                return str(cursor.lastrowid)
    
    def get_all_devices(self) -> List[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('SELECT * FROM devices')
            return [dict(row) for row in cursor.fetchall()]
    
    def get_device_by_id(self, device_id: str) -> Optional[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('SELECT * FROM devices WHERE id = ?', (device_id,))
            row = cursor.fetchone()
            return dict(row) if row else None
    
    def update_device(self, device_id: str, **kwargs) -> bool:
        updates = []
        params = []
        
        allowed_fields = ['name', 'ip_address', 'mac_address', 'organization_id', 
                         'status', 'os_version', 'app_version', 'dotnet_version',
                         'last_heartbeat', 'last_seen', 'exit_time', 'offline_reason',
                         'contact_phone', 'class_name', 'person_in_charge',
                         'cpu_usage', 'memory_usage', 'disk_usage']
        
        for field, value in kwargs.items():
            if field in allowed_fields:
                updates.append(f'{field} = ?')
                params.append(value)
        
        if not updates:
            return False
        
        params.append(device_id)
        
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute(f'UPDATE devices SET {", ".join(updates)} WHERE id = ?', params)
            conn.commit()
            return cursor.rowcount > 0
    
    def delete_device(self, device_id: str) -> bool:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('DELETE FROM devices WHERE id = ?', (device_id,))
            conn.commit()
            return cursor.rowcount > 0
    
    def get_devices_by_organization(self, org_id: str) -> List[Dict[str, Any]]:
        with self.get_connection() as conn:
            cursor = conn.cursor()
            cursor.execute('SELECT * FROM devices WHERE organization_id = ?', (org_id,))
            return [dict(row) for row in cursor.fetchall()]
    
    def migrate_from_json(self, json_dir: str) -> Dict[str, Any]:
        import json
        import os
        from datetime import datetime
        
        result = {
            'accounts': 0,
            'organizations': 0,
            'devices': 0,
            'errors': []
        }
        
        try:
            # 迁移账户
            users_file = os.path.join(json_dir, 'users.json')
            if os.path.exists(users_file):
                with open(users_file, 'r', encoding='utf-8') as f:
                    users = json.load(f)
                    for username, user_data in users.items():
                        try:
                            if not self.get_account_by_username(username):
                                self.create_account(
                                    username=username,
                                    password=user_data['password'],  # 已经是哈希过的密码
                                    role=user_data['role']
                                )
                                result['accounts'] += 1
                        except Exception as e:
                            result['errors'].append(f"Failed to migrate user {username}: {str(e)}")
            
            # 迁移组织
            orgs_file = os.path.join(json_dir, 'organizations.json')
            if os.path.exists(orgs_file):
                with open(orgs_file, 'r', encoding='utf-8') as f:
                    orgs = json.load(f)
                    for org_id, org_data in orgs.items():
                        try:
                            # 先检查组织是否存在
                            existing_org = self.get_organization_by_id(org_id)
                            if not existing_org:
                                self.create_organization(
                                    name=org_data['name'],
                                    description=org_data.get('description', ''),
                                    server_url=None
                                )
                                result['organizations'] += 1
                            
                            # 迁移组织配置（保存完整的 securityConfig）
                            if org_data.get('securityConfig'):
                                self.save_organization_config(
                                    org_id=org_id,
                                    security_config=org_data['securityConfig'],
                                    network_config=org_data.get('networkConfig')
                                )
                        except Exception as e:
                            result['errors'].append(f"Failed to migrate org {org_id}: {str(e)}")
            
            # 迁移设备
            devices_file = os.path.join(json_dir, 'devices.json')
            if os.path.exists(devices_file):
                with open(devices_file, 'r', encoding='utf-8') as f:
                    devices = json.load(f)
                    for device_id, device_data in devices.items():
                        try:
                            # 检查设备是否存在
                            existing_device = self.get_device_by_id(device_id)
                            
                            # 创建设备
                            self.create_device(
                                name=device_data['name'],
                                ip_address=device_data.get('ipAddress', ''),
                                organization_id=device_data.get('organizationId'),
                                device_id=device_id,
                                mac_address=device_data.get('macAddress'),
                                os_version=device_data.get('osVersion'),
                                app_version=device_data.get('appVersion'),
                                dotnet_version=device_data.get('dotnetVersion'),
                                contact_phone=device_data.get('contactPhone'),
                                class_name=device_data.get('className'),
                                person_in_charge=device_data.get('personInCharge')
                            )
                            
                            # 更新设备状态和心跳信息
                            update_data = {}
                            if device_data.get('status'):
                                update_data['status'] = device_data['status']
                            if device_data.get('lastHeartbeat'):
                                update_data['last_heartbeat'] = device_data['lastHeartbeat']
                            if device_data.get('lastSeen'):
                                update_data['last_seen'] = device_data['lastSeen']
                            if device_data.get('offlineReason'):
                                update_data['offline_reason'] = device_data['offlineReason']
                            if device_data.get('exitTime'):
                                update_data['exit_time'] = device_data['exitTime']
                            if device_data.get('cpuUsage') is not None:
                                update_data['cpu_usage'] = float(device_data['cpuUsage'])
                            if device_data.get('memoryUsage') is not None:
                                update_data['memory_usage'] = float(device_data['memoryUsage'])
                            if device_data.get('diskUsage') is not None:
                                update_data['disk_usage'] = float(device_data['diskUsage'])
                            
                            if update_data:
                                self.update_device(device_id, **update_data)
                            
                            result['devices'] += 1
                        except Exception as e:
                            result['errors'].append(f"Failed to migrate device {device_id}: {str(e)}")
        
        except Exception as e:
            result['errors'].append(f"Migration failed: {str(e)}")
        
        return result

db = Database()
