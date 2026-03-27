#!/bin/bash

# Скрипт ініціалізації нового сервера AWS Lightsail (Ubuntu 22.04)
# Цей скрипт встановлює .NET SDK 8.0, tmux та готує папку для бота.

echo "--- Початок налаштування сервера ---"

# 1. Оновлення системи
sudo apt update && sudo apt upgrade -y

# 2. Встановлення .NET SDK 8.0
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
rm packages-microsoft-prod.deb
sudo apt update
sudo apt install -y dotnet-sdk-8.0

# 3. Встановлення tmux
sudo apt install -y tmux

# 4. Створення папки для бота та налаштування прав
mkdir -p /home/ubuntu/ArbitrageBot
sudo chown -R ubuntu:ubuntu /home/ubuntu/ArbitrageBot

# 5. Перевірка версії .NET
echo "--- Перевірка встановленого .NET ---"
dotnet --version

echo "--- Налаштування завершено! ---"
echo "Тепер ви можете налаштувати GitHub Secrets (SERVER_IP та SSH_PRIVATE_KEY) для автоматичного деплою."
