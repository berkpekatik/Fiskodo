export default function StatCardSkeleton() {
  return (
    <div className="rounded-xl bg-[#36393f] p-5 animate-pulse">
      <div className="flex items-center gap-2 mb-2">
        <div className="w-5 h-5 rounded bg-[#40444b]" />
        <div className="h-3 w-16 bg-[#40444b] rounded" />
      </div>
      <div className="h-8 w-20 bg-[#40444b] rounded" />
    </div>
  )
}
