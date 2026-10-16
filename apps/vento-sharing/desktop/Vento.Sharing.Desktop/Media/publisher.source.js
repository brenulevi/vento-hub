import { Room, RoomEvent, Track } from 'livekit-client';

const data = JSON.parse(document.getElementById('connection').textContent);
const startButton = document.getElementById('start');
const stopButton = document.getElementById('stop');
const status = document.getElementById('status');
const preview = document.getElementById('preview');
const room = new Room({ dynacast: true });
let stream;
let active = false;
let reporting;
let stopping = false;

async function report(state) {
  try {
    await fetch('status', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ state }),
      keepalive: state !== 'publishing',
    });
  } catch { /* A sessão será encerrada caso o desktop desapareça. */ }
}

async function stop() {
  if (stopping) return;
  stopping = true;
  clearInterval(reporting);
  active = false;
  startButton.disabled = true;
  stopButton.disabled = true;
  stream?.getTracks().forEach(track => track.stop());
  stream = undefined;
  preview.srcObject = null;
  await room.disconnect();
  await report('stopped');
  status.textContent = 'Transmissão encerrada. Você pode fechar esta janela.';
}

room.on(RoomEvent.Disconnected, () => {
  if (active) void stop();
});

startButton.addEventListener('click', async () => {
  startButton.disabled = true;
  try {
    stream = await navigator.mediaDevices.getDisplayMedia({
      video: { frameRate: { ideal: 15, max: 15 } },
      audio: false,
    });
    const track = stream.getVideoTracks()[0];
    if (!track) throw new Error('Nenhuma tela foi selecionada.');
    track.addEventListener('ended', () => void stop(), { once: true });
    await room.localParticipant.publishTrack(track, {
      source: Track.Source.ScreenShare,
      simulcast: false,
      videoEncoding: { maxBitrate: 1_000_000, maxFramerate: 15 },
    });
    preview.srcObject = stream;
    active = true;
    stopButton.disabled = false;
    status.textContent = 'Transmitindo vídeo. Áudio desligado.';
    await report('publishing');
    clearInterval(reporting);
    reporting = setInterval(() => void report('publishing'), 15_000);
  } catch (error) {
    stream?.getTracks().forEach(track => track.stop());
    stream = undefined;
    status.textContent = error?.name === 'NotAllowedError'
      ? 'Seleção de tela cancelada.' : 'Não foi possível iniciar a transmissão.';
    startButton.disabled = false;
    void report('error');
  }
});
stopButton.addEventListener('click', () => void stop());

async function control() {
  try {
    const response = await fetch('control', { cache: 'no-store' });
    if (response.ok && (await response.json()).stop) await stop();
  } catch {
    if (active) await stop();
  }
}
setInterval(() => void control(), 2_000);
window.addEventListener('pagehide', () => {
  void report('stopped');
  stream?.getTracks().forEach(track => track.stop());
  room.disconnect();
});

(async () => {
  try {
    await room.connect(data.Url, data.Token);
    status.textContent = 'Conectado. Clique para escolher uma tela ou janela.';
    startButton.disabled = false;
    await report('pending');
    reporting = setInterval(() => void report(active ? 'publishing' : 'pending'), 15_000);
  } catch (error) {
    console.warn('Publisher connection failed', error);
    status.textContent = 'Não foi possível conectar ao servidor de mídia. Confira as portas WebRTC.';
    void report('error');
  }
})();
