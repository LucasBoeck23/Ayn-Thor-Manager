// Ayn Thor Manager — Frontend
const API = '';
let currentPath = '/storage/';
let ws = null;
let selectedEntry = null;

// === DOM Elements ===
const $ = id => document.getElementById(id);
const statusDot = $('status-dot');
const statusText = $('status-text');
const ipInput = $('ip-input');
const btnConnect = $('btn-connect');
const btnDisconnect = $('btn-disconnect');
const connMsg = $('connection-message');
const pathInput = $('path-input');
const fileList = $('file-list');
const uploadProgress = $('upload-progress');
const progressFill = $('progress-fill');
const uploadFileName = $('upload-file-name');
const uploadPercent = $('upload-percent');
const uploadSpeed = $('upload-speed');
const uploadIndex = $('upload-index');
const contextMenu = $('context-menu');
const modalOverlay = $('modal-overlay');
const modalTitle = $('modal-title');
const modalInput = $('modal-input');

// === API Helpers ===
async function api(path, options = {}) {
    const res = await fetch(API + path, options);
    const data = res.headers.get('content-type')?.includes('json') ? await res.json() : null;
    return { ok: res.ok, status: res.status, data };
}

function formatSize(bytes) {
    if (bytes === 0) return '-';
    const units = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(1024));
    return (bytes / Math.pow(1024, i)).toFixed(i > 0 ? 1 : 0) + ' ' + units[i];
}

// === Status ===
function updateStatus(status, ip, message) {
    statusDot.className = 'dot ' + (status === 'conectado' ? 'connected' : status === 'não autorizado' ? 'unauthorized' : 'disconnected');
    statusText.textContent = status.charAt(0).toUpperCase() + status.slice(1) + (ip ? ` (${ip})` : '');
    const connected = status === 'conectado';
    btnConnect.disabled = connected;
    btnDisconnect.disabled = !connected;
    if (connected) loadDirectory();
    else fileList.innerHTML = '<p class="placeholder">Conecte ao dispositivo para navegar os arquivos.</p>';
}

async function refreshStatus() {
    const { data } = await api('/api/device/status');
    if (data) updateStatus(data.status, data.ipAddress, data.message);
}

// === Connection ===
btnConnect.addEventListener('click', async () => {
    const ip = ipInput.value.trim();
    if (!ip) { showMsg('Digite o IP do Thor', 'error'); return; }
    btnConnect.disabled = true;
    showMsg('Conectando...', '');
    const { ok, data } = await api('/api/device/connect', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ ipAddress: ip })
    });
    if (ok) {
        updateStatus(data.status, data.ipAddress, data.message);
        showMsg(data.status === 'conectado' ? 'Conectado!' : data.message || 'Falha', data.status === 'conectado' ? 'success' : 'error');
    } else {
        showMsg(data?.detail || 'Erro ao conectar', 'error');
        btnConnect.disabled = false;
    }
});

btnDisconnect.addEventListener('click', async () => {
    const { ok, data } = await api('/api/device/disconnect', { method: 'POST' });
    if (ok) {
        updateStatus('desconectado', null, null);
        showMsg('Desconectado', '');
    }
});

// === Network Scan ===
$('btn-scan').addEventListener('click', async () => {
    const scanBtn = $('btn-scan');
    const scanResults = $('scan-results');
    scanBtn.disabled = true;
    scanBtn.textContent = '⏳ Buscando e conectando...';
    scanResults.hidden = false;
    scanResults.innerHTML = '<p style="color:#78909c;padding:8px">Varrendo a rede e tentando conectar ao Thor...</p>';

    const { ok, data } = await api('/api/device/scan');
    scanBtn.disabled = false;
    scanBtn.innerHTML = '&#128269; Buscar';

    if (!ok || !data.devices || data.devices.length === 0) {
        scanResults.innerHTML = `<p style="color:#ff5252;padding:8px">Nenhum dispositivo encontrado.<br><small>Verifique se o Thor esta ligado com "Depuracao sem fio" ativa.</small></p>`;
        return;
    }

    // Auto-connected! Update UI
    if (data.autoConnected) {
        const d = data.devices[0];
        scanResults.innerHTML = `<p style="color:#00e676;padding:8px">✔ Conectado a <strong>${d.name}</strong> (${d.address})</p>`;
        ipInput.value = d.address;
        updateStatus('conectado', d.address, null);
        return;
    }

    // Show found devices for manual selection
    scanResults.innerHTML = data.devices.map(d => `
        <div class="device-item" data-ip="${d.address}">
            <div style="display:flex;flex-direction:column;gap:2px">
                <span class="device-ip" style="font-size:0.95rem">${d.name}</span>
                <span class="device-source">${d.address} — ${d.status}</span>
            </div>
        </div>
    `).join('');

    scanResults.querySelectorAll('.device-item').forEach(el => {
        el.addEventListener('click', () => {
            ipInput.value = el.dataset.ip;
            scanResults.hidden = true;
            btnConnect.click();
        });
    });
});

function showMsg(text, type) {
    connMsg.textContent = text;
    connMsg.className = 'message' + (type ? ' ' + type : '');
}

// === Pair ===
$('btn-pair').addEventListener('click', () => {
    const host = prompt('IP:Porta de pareamento do Thor\n(aparece em "Parear com codigo" nas opcoes de desenvolvedor):', '192.168.1.100:37000');
    if (!host) return;
    const code = prompt('Codigo de pareamento (6 digitos que aparece no Thor):');
    if (!code) return;

    showMsg('Pareando...', '');
    api('/api/device/pair', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ host, code })
    }).then(({ ok, data }) => {
        if (data?.success) {
            showMsg('Pareado! Agora conecte usando o IP + porta de conexao.', 'success');
        } else {
            showMsg(data?.message || 'Falha no pareamento', 'error');
        }
    });
});

// === File Browser ===
async function loadDirectory(path) {
    if (path) currentPath = path;
    pathInput.value = currentPath;
    fileList.innerHTML = '<p class="placeholder">Carregando...</p>';
    const { ok, data } = await api(`/api/files?path=${encodeURIComponent(currentPath)}`);
    if (!ok) {
        fileList.innerHTML = `<p class="placeholder">${data?.detail || 'Erro ao listar'}</p>`;
        return;
    }
    if (data.entries.length === 0) {
        fileList.innerHTML = '<p class="placeholder">Pasta vazia</p>';
        return;
    }
    fileList.innerHTML = data.entries.map(e => `
        <div class="file-entry" data-path="${currentPath}${e.name}" data-type="${e.type}" data-name="${e.name}">
            <span class="icon">${e.type === 'directory' ? '&#128193;' : '&#128196;'}</span>
            <span class="name">${e.name}</span>
            <span class="meta">${e.type === 'file' ? formatSize(e.sizeBytes) : ''}</span>
        </div>
    `).join('');
    // Events
    fileList.querySelectorAll('.file-entry').forEach(el => {
        el.addEventListener('dblclick', () => {
            if (el.dataset.type === 'directory') loadDirectory(el.dataset.path + '/');
        });
        el.addEventListener('contextmenu', e => {
            e.preventDefault();
            selectedEntry = { path: el.dataset.path, name: el.dataset.name, type: el.dataset.type };
            contextMenu.style.left = e.clientX + 'px';
            contextMenu.style.top = e.clientY + 'px';
            contextMenu.hidden = false;
        });
    });
}

$('btn-up').addEventListener('click', () => {
    const parts = currentPath.replace(/\/$/, '').split('/');
    if (parts.length > 2) {
        parts.pop();
        loadDirectory(parts.join('/') + '/');
    }
});

$('btn-refresh').addEventListener('click', () => loadDirectory());

// === New Folder ===
$('btn-new-folder').addEventListener('click', () => showModal('Nova Pasta', 'Nome da pasta', async (name) => {
    const { ok, data } = await api('/api/files/directory', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ parentPath: currentPath, name })
    });
    if (ok) loadDirectory();
    else alert(data?.detail || 'Erro ao criar pasta');
}));

// === Context Menu ===
document.addEventListener('click', () => { contextMenu.hidden = true; });

$('ctx-rename').addEventListener('click', () => {
    if (!selectedEntry) return;
    contextMenu.hidden = true;
    showModal('Renomear', selectedEntry.name, async (newName) => {
        const { ok, data } = await api('/api/files/rename', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ currentPath: selectedEntry.path, newName })
        });
        if (ok) loadDirectory();
        else alert(data?.detail || 'Erro ao renomear');
    });
});

$('ctx-delete').addEventListener('click', async () => {
    if (!selectedEntry) return;
    contextMenu.hidden = true;
    if (!confirm(`Excluir "${selectedEntry.name}"?`)) return;
    const { ok, data } = await api(`/api/files?path=${encodeURIComponent(selectedEntry.path)}`, { method: 'DELETE' });
    if (ok) loadDirectory();
    else alert(data?.detail || 'Erro ao excluir');
});

// === Upload ===
$('file-upload-input').addEventListener('change', async (e) => {
    const files = e.target.files;
    if (!files.length) return;
    const formData = new FormData();
    for (const f of files) formData.append('file', f);
    uploadProgress.hidden = false;
    uploadFileName.textContent = files[0].name;
    uploadPercent.textContent = '0%';
    progressFill.style.width = '0%';

    const res = await fetch(`/api/files/upload?destination=${encodeURIComponent(currentPath)}`, {
        method: 'POST',
        body: formData
    });
    if (!res.ok) {
        const err = await res.json().catch(() => null);
        alert(err?.detail || 'Erro no upload');
        uploadProgress.hidden = true;
    }
    e.target.value = '';
});

$('btn-cancel-upload').addEventListener('click', async () => {
    await api('/api/files/upload/cancel', { method: 'POST' });
});

// === Modal ===
let modalCallback = null;
function showModal(title, defaultValue, callback) {
    modalTitle.textContent = title;
    modalInput.value = defaultValue || '';
    modalCallback = callback;
    modalOverlay.classList.add('active');
    modalInput.focus();
    modalInput.select();
}

$('modal-confirm').addEventListener('click', () => {
    const val = modalInput.value.trim();
    if (val && modalCallback) modalCallback(val);
    modalOverlay.classList.remove('active');
});
$('modal-cancel').addEventListener('click', () => { modalOverlay.classList.remove('active'); });
modalInput.addEventListener('keydown', e => { if (e.key === 'Enter') $('modal-confirm').click(); });

// === WebSocket ===
function connectWs() {
    const proto = location.protocol === 'https:' ? 'wss:' : 'ws:';
    ws = new WebSocket(`${proto}//${location.host}/ws`);
    ws.onmessage = (e) => {
        const msg = JSON.parse(e.data);
        if (msg.type === 'transfer_progress') {
            uploadProgress.hidden = false;
            uploadFileName.textContent = msg.payload.fileName;
            uploadPercent.textContent = msg.payload.percentComplete + '%';
            progressFill.style.width = msg.payload.percentComplete + '%';
            uploadSpeed.textContent = formatSize(msg.payload.speedBytesPerSecond) + '/s';
            uploadIndex.textContent = `${msg.payload.currentFileIndex}/${msg.payload.totalFiles}`;
        }
        if (msg.type === 'transfer_completed') {
            uploadProgress.hidden = true;
            loadDirectory();
        }
        if (msg.type === 'transfer_failed' || msg.type === 'transfer_cancelled') {
            uploadProgress.hidden = true;
            alert('Upload falhou ou foi cancelado');
        }
        if (msg.type === 'device_status') {
            updateStatus(msg.payload.status, msg.payload.ipAddress, msg.payload.message);
        }
    };
    ws.onclose = () => setTimeout(connectWs, 3000);
}

// === Init ===
refreshStatus();
connectWs();

// === Streaming ===
const btnStreamStart = $('btn-stream-start');
const btnStreamStop = $('btn-stream-stop');
const streamStatus = $('stream-status');

btnStreamStart.addEventListener('click', async () => {
    btnStreamStart.disabled = true;
    streamStatus.textContent = 'Iniciando...';
    streamStatus.style.color = '#78909c';
    const { ok, data } = await api('/api/stream/start', { method: 'POST' });
    if (ok) {
        btnStreamStop.disabled = false;
        streamStatus.textContent = 'Ativo (janela aberta)';
        streamStatus.style.color = '#00e676';
        // Abre aba de controle
        window.open('/stream.html', '_blank');
    } else {
        btnStreamStart.disabled = false;
        streamStatus.textContent = data?.detail || 'Erro';
        streamStatus.style.color = '#ff5252';
    }
});

btnStreamStop.addEventListener('click', async () => {
    const { ok } = await api('/api/stream/stop', { method: 'POST' });
    if (ok) {
        btnStreamStart.disabled = false;
        btnStreamStop.disabled = true;
        streamStatus.textContent = 'Inativo';
        streamStatus.style.color = '#78909c';
    }
});

// Check stream status on load
api('/api/stream/status').then(({ data }) => {
    if (data?.streaming) {
        btnStreamStart.disabled = false;
        btnStreamStop.disabled = false;
        streamStatus.textContent = 'Ativo (janela aberta)';
        streamStatus.style.color = '#00e676';
    }
});
