#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
app_dll="$repo_dir/apps/vento-sharing/desktop/Vento.Sharing.Desktop/bin/Release/net10.0/Vento.Sharing.Desktop.dll"
if [[ ! -f "$app_dll" ]]; then
    echo "Compile primeiro: dotnet build Vento.slnx -c Release" >&2
    exit 1
fi

dotnet_bin="$(command -v dotnet || true)"
if [[ -z "$dotnet_bin" && -x /tmp/vento-dotnet/dotnet ]]; then
    dotnet_bin=/tmp/vento-dotnet/dotnet
fi
if [[ -z "$dotnet_bin" ]]; then
    echo "Não encontrei dotnet no PATH." >&2
    exit 1
fi

applications_dir="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$applications_dir"
desktop_file="$applications_dir/vento-sharing.desktop"
cat > "$desktop_file" <<EOF
[Desktop Entry]
Type=Application
Name=Vento Sharing
Comment=Compartilhamento de tela do Vento
Exec="$dotnet_bin" "$app_dll" %u
Terminal=false
Categories=Network;
MimeType=x-scheme-handler/vento-sharing;
EOF
xdg-mime default vento-sharing.desktop x-scheme-handler/vento-sharing
echo "Protocolo vento-sharing:// registrado para este usuário."
