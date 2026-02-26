import type { VoiceConnectionDto } from '@/types/api'

type VoiceSessionCardProps = {
  connection: VoiceConnectionDto
  onDisconnect?: (guildId: string) => void
}

export default function VoiceSessionCard({ connection, onDisconnect }: VoiceSessionCardProps) {
  const guildName = connection.guildName ?? 'Unknown Server'
  const channelName = connection.channelName ?? 'Unknown Channel'
  const { userCount, playback } = connection
  const isIdle = !playback.nowPlayingTitle && playback.queueCount === 0

  return (
    <div className="rounded-xl bg-[#36393f] p-5 flex flex-col gap-4">
      <div className="flex items-start justify-between">
        <div className="min-w-0 flex-1">
          <p className="text-xs text-[#b9bbbe] flex items-center gap-1.5 mb-1">
            <span className="w-2 h-2 rounded-full bg-green-500" />
            ACTIVE NOW
          </p>
          <h3 className="text-lg font-semibold text-white">{guildName}</h3>
          <p className="text-sm text-[#b9bbbe] flex items-center gap-1.5 mt-1">
            <svg className="w-4 h-4 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20" aria-hidden>
              <path d="M9.383 3.076A1 1 0 0110 4v12a1 1 0 01-1.617.776L4.235 13H1a1 1 0 01-1-1V8a1 1 0 011-1h3.235l4.148-3.776a1 1 0 011-.148z" />
            </svg>
            Channel: {channelName}
          </p>
          <p className="text-xs text-[#72767d] mt-0.5">
            {userCount} {userCount === 1 ? 'user' : 'users'} in channel
          </p>
          {/* Playback */}
          <div className="mt-3 rounded-lg bg-[#2c2f33]/80 px-3 py-2 text-sm">
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-[#b9bbbe]">
              <span className="font-medium text-white">
                {isIdle ? 'Idle' : playback.nowPlayingTitle ?? '—'}
              </span>
              <span>Queue: {playback.queueCount}</span>
              {playback.shuffle && (
                <span className="text-[#9b59b6]">Shuffle on</span>
              )}
              {playback.isPlaylistSession && (
                <span className="rounded bg-[#5865f2]/30 px-1.5 py-0.5 text-xs text-[#b9bbbe]">
                  Playlist
                </span>
              )}
            </div>
          </div>
        </div>
        <div className="w-10 h-10 rounded-lg bg-[#5865f2] flex items-center justify-center flex-shrink-0 overflow-hidden">
          {connection.guildIconUrl ? (
            <img
              src={connection.guildIconUrl}
              alt=""
              className="w-full h-full object-cover"
            />
          ) : (
            <svg className="w-5 h-5 text-white" viewBox="0 0 24 24" fill="currentColor" aria-hidden>
              <path d="M20.317 4.37a19.791 19.791 0 00-4.885-1.515.074.074 0 00-.079.037c-.21.375-.444.864-.608 1.25a18.27 18.27 0 00-5.487 0 12.64 12.64 0 00-.617-1.25.077.077 0 00-.079-.037A19.736 19.736 0 003.677 4.37a.07.07 0 00-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 00.031.057 19.9 19.9 0 005.993 3.03.078.078 0 00.084-.028c.462-.63.874-1.295 1.226-1.994a.076.076 0 00-.041-.106 13.107 13.107 0 01-1.872-.892.077.077 0 01-.008-.128 10.2 10.2 0 00.372-.292.074.074 0 01.077-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 01.078.01c.12.098.246.198.373.292a.077.077 0 01-.006.127 12.299 12.299 0 01-1.873.892.077.077 0 00-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 00.084.028 19.839 19.839 0 006.002-3.03.077.077 0 00.032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 00-.031-.03z" />
            </svg>
          )}
        </div>
      </div>
      {onDisconnect && (
        <button
          type="button"
          onClick={() => onDisconnect(connection.guildId)}
          className="w-full py-2.5 rounded-lg bg-[#ed4245] hover:bg-[#c03537] text-white font-medium flex items-center justify-center gap-2 transition-colors"
        >
          <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
          </svg>
          Disconnect
        </button>
      )}
    </div>
  )
}
