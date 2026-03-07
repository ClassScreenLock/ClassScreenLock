#!/bin/bash

echo "================================================"
echo "  ClassScreenLock Web 集控管理平台 - 一键启动"
echo "================================================"
echo ""

# 检查 Python 是否安装
if ! command -v python3 &> /dev/null; then
    echo "[错误] 未检测到 Python3，请先安装 Python 3.8+"
    echo "macOS: brew install python@3.11"
    echo "Ubuntu/Debian: sudo apt-get install python3 python3-pip"
    exit 1
fi

# 检查 Node.js 是否安装
if ! command -v node &> /dev/null; then
    echo "[错误] 未检测到 Node.js，请先安装 Node.js 18+"
    echo "macOS: brew install node"
    echo "Ubuntu/Debian: curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash - && sudo apt-get install -y nodejs"
    exit 1
fi

echo "[✓] Python 已安装：$(python3 --version)"
echo "[✓] Node.js 已安装：$(node --version)"
echo ""

# 检查依赖是否安装
echo "正在检查依赖..."

if [ ! -d "node_modules" ]; then
    echo "[!] 前端依赖未安装，正在安装..."
    npm install
    if [ $? -ne 0 ]; then
        echo "[错误] 前端依赖安装失败"
        exit 1
    fi
    echo "[✓] 前端依赖安装完成"
else
    echo "[✓] 前端依赖已安装"
fi

if [ ! -d "backend/__pycache__" ]; then
    echo "[!] Python 依赖未安装，正在安装..."
    cd backend
    pip3 install -r requirements.txt
    if [ $? -ne 0 ]; then
        echo "[错误] Python 依赖安装失败"
        cd ..
        exit 1
    fi
    cd ..
    echo "[✓] Python 依赖安装完成"
else
    echo "[✓] Python 依赖已安装"
fi

echo ""
echo "================================================"
echo "  正在启动服务..."
echo "================================================"
echo ""

# 启动 Python 后端
echo "[1/2] 启动 Python 后端..."
cd backend
python3 app.py &
BACKEND_PID=$!
cd ..

# 等待后端启动
echo "等待后端启动..."
sleep 3

# 启动前端开发服务器
echo "[2/2] 启动前端开发服务器..."
npm run dev &
FRONTEND_PID=$!

echo ""
echo "================================================"
echo "  服务启动完成！"
echo "================================================"
echo ""
echo "前端地址：http://localhost:5173"
echo "后端地址：http://localhost:5000"
echo ""
echo "默认账户:"
echo "  用户名：admin"
echo "  密码：admin123"
echo ""
echo "⚠️  提示：首次登录后请立即修改默认密码！"
echo ""
echo "进程 ID:"
echo "  后端：$BACKEND_PID"
echo "  前端：$FRONTEND_PID"
echo ""
echo "按 Ctrl+C 停止所有服务"
echo ""

# 等待用户中断
trap "kill $BACKEND_PID $FRONTEND_PID 2>/dev/null; echo ''; echo '服务已停止'; exit" INT

# 保持脚本运行
wait
