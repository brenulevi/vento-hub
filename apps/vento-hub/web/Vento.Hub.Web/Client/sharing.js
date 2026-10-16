import { Room, RoomEvent, Track } from 'livekit-client';

const connection = JSON.parse(document.getElementById('media-connection').textContent);
const video = document.getElementById('shared-video');
const emptyStage = document.getElementById('empty-stage');
const status = document.getElementById('media-status');
const room = new Room({ adaptiveStream: true });

room.on(RoomEvent.TrackSubscribed, (track, publication) => {
  if (track.kind !== Track.Kind.Video || publication.source !== Track.Source.ScreenShare) return;
  track.attach(video);
  video.classList.add('is-visible');
  emptyStage.hidden = true;
  status.textContent = 'Assistindo à transmissão.';
  video.play().catch(() => {});
});

room.on(RoomEvent.TrackUnsubscribed, (track) => {
  if (track.kind !== Track.Kind.Video) return;
  track.detach(video);
  video.classList.remove('is-visible');
  video.srcObject = null;
  emptyStage.hidden = false;
  status.textContent = 'Aguardando transmissão.';
});

room.on(RoomEvent.Disconnected, () => {
  video.srcObject = null;
  video.classList.remove('is-visible');
  emptyStage.hidden = false;
  status.textContent = 'Conexão encerrada. Atualize a página para entrar novamente.';
});

async function connect() {
  try {
    await room.connect(connection.Url, connection.Token);
    status.textContent = 'Conectado. Aguardando transmissão.';
  } catch (error) {
    console.warn('Media connection failed', error);
    status.textContent = 'Não foi possível estabelecer a conexão de mídia. Verifique o LiveKit e as portas WebRTC.';
  }
}

connect();
window.addEventListener('pagehide', () => room.disconnect());
