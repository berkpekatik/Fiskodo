export type AuthRequest = { username: string; password: string }
export type AuthResponse = { token: string; expiresAt: string }

export type PlaybackInfoDto = {
  nowPlayingTitle: string | null
  queueCount: number
  isPlaylistSession: boolean
  shuffle: boolean
}

export type VoiceConnectionDto = {
  guildId: string
  guildName: string | null
  guildIconUrl: string | null
  channelId: string
  channelName: string | null
  userCount: number
  playback: PlaybackInfoDto
}

export type BotStatusDto = {
  botUserId: string
  shardId: number | null
  shardCount: number
  connectedGuilds: number
  activeVoiceConnections: number
  uptime: string
  latency: string
  voiceConnections: VoiceConnectionDto[]
}
