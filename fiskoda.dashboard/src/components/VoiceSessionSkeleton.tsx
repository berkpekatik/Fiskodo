export default function VoiceSessionSkeleton() {
  return (
    <div className="rounded-xl bg-[#36393f] p-5 animate-pulse space-y-4">
      <div className="flex justify-between">
        <div className="space-y-2">
          <div className="h-3 w-24 bg-[#40444b] rounded" />
          <div className="h-5 w-32 bg-[#40444b] rounded" />
          <div className="h-4 w-40 bg-[#40444b] rounded" />
        </div>
        <div className="w-10 h-10 rounded-lg bg-[#40444b]" />
      </div>
      <div className="h-10 w-full bg-[#40444b] rounded-lg" />
    </div>
  )
}
