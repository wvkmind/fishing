#!/bin/bash
# 服务器一键部署脚本
# 用法: bash deploy.sh

SERVER="root@47.95.178.225"
LOCAL_BUILD="D:/Unity/Project/server"
REMOTE_PATH="/root/server"

echo "[1/3] Stopping old server..."
ssh $SERVER "pkill -f server.x86_64 || true"
sleep 1

echo "[2/3] Uploading build..."
scp -r "$LOCAL_BUILD" $SERVER:/root/

echo "[3/3] Starting server..."
ssh $SERVER "chmod +x $REMOTE_PATH/server.x86_64; cd $REMOTE_PATH; nohup ./server.x86_64 -batchmode -nographics > /root/server.log 2>&1 &"
sleep 3

echo "--- Verifying ---"
ssh $SERVER "ps aux | grep server.x86_64 | grep -v grep && echo 'Server is running' || echo 'ERROR: Server not running'"
echo ""
echo "--- Last 5 log lines ---"
ssh $SERVER "tail -5 /root/server.log"
echo ""
echo "Deploy done."
