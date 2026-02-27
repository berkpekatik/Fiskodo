import { Link } from 'react-router-dom'
import { motion } from 'framer-motion'

const BOT_INVITE_URL = 'https://discord.com/oauth2/authorize?client_id=374196306975129601&permissions=274881072128&integration_type=0&scope=bot+applications.commands'

export default function LandingPage() {
  return (
    <div className="min-h-screen bg-[#2c2f33] text-white flex flex-col relative">
      {/* Admin button - top right */}
      <div className="absolute top-4 right-4">
        <Link
          to="/login"
          className="text-sm text-[#b9bbbe] hover:text-white px-3 py-1.5 rounded-md hover:bg-[#40444b] transition-colors"
        >
          Admin
        </Link>
      </div>

      {/* Main content */}
      <main className="flex-1 flex flex-col items-center justify-center px-6 py-12">
        <motion.div
          className="text-center max-w-md"
          initial={{ opacity: 0, y: 12 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.4 }}
        >
          <h1 className="text-3xl font-bold mb-2">Fiskoda</h1>
          <p className="text-[#b9bbbe] text-sm mb-10">
            Add the bot to your Discord server and enjoy music and more.
          </p>

          <motion.a
            href={BOT_INVITE_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center justify-center gap-2.5 px-8 py-4 rounded-xl bg-[#5865f2] hover:bg-[#4752c4] text-white font-semibold text-lg shadow-lg shadow-[#5865f2]/25 transition-colors"
            whileHover={{ scale: 1.03 }}
            whileTap={{ scale: 0.98 }}
            animate={{
              boxShadow: [
                '0 10px 40px -10px rgba(88, 101, 242, 0.25)',
                '0 10px 50px -8px rgba(88, 101, 242, 0.4)',
                '0 10px 40px -10px rgba(88, 101, 242, 0.25)',
              ],
            }}
            transition={{
              boxShadow: { duration: 2, repeat: Infinity, repeatType: 'reverse' },
            }}
          >
            <svg className="w-6 h-6" viewBox="0 0 24 24" fill="currentColor" aria-hidden>
              <path d="M20.317 4.37a19.791 19.791 0 00-4.885-1.515.074.074 0 00-.079.037c-.21.375-.444.864-.608 1.25a18.27 18.27 0 00-5.487 0 12.64 12.64 0 00-.617-1.25.077.077 0 00-.079-.037A19.736 19.736 0 003.677 4.37a.07.07 0 00-.032.027C.533 9.046-.32 13.58.099 18.057a.082.082 0 00.031.057 19.9 19.9 0 005.993 3.03.078.078 0 00.084-.028c.462-.63.874-1.295 1.226-1.994a.076.076 0 00-.041-.106 13.107 13.107 0 01-1.872-.892.077.077 0 01-.008-.128 10.2 10.2 0 00.372-.292.074.074 0 01.077-.01c3.928 1.793 8.18 1.793 12.062 0a.074.074 0 01.078.01c.12.098.246.198.373.292a.077.077 0 01-.006.127 12.299 12.299 0 01-1.873.892.077.077 0 00-.041.107c.36.698.772 1.362 1.225 1.993a.076.076 0 00.084.028 19.839 19.839 0 006.002-3.03.077.077 0 00.032-.054c.5-5.177-.838-9.674-3.549-13.66a.061.061 0 00-.031-.03z" />
            </svg>
            Invite Fiskoda
          </motion.a>
        </motion.div>
      </main>
    </div>
  )
}
