import { Link } from 'react-router-dom'
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome'
import { faDiscord } from '@fortawesome/free-brands-svg-icons'
import { faUniversalAccess } from '@fortawesome/free-solid-svg-icons'
import { motion } from 'framer-motion'

const BOT_INVITE_URL = 'https://discord.com/oauth2/authorize?client_id=374196306975129601&permissions=274881072128&integration_type=0&scope=bot+applications.commands'

const fadeInUp = {
  initial: { opacity: 0, y: 16 },
  animate: { opacity: 1, y: 0 },
}

export default function LandingPage() {
  return (
    <div className="min-h-screen bg-[#2c2f33] text-white flex flex-col">
      {/* Top bar: Accessibility + Admin */}
      <header className="flex items-center justify-end gap-1 px-4 py-3 border-b border-[#202225]/50">
        
        <Link
          to="/login"
          className="text-sm text-[#b9bbbe] hover:text-white px-3 py-2 rounded-lg hover:bg-[#40444b] transition-colors"
        >
          <a
          className="p-2 rounded-lg text-[#b9bbbe] hover:text-white hover:bg-[#40444b] transition-colors focus:outline-none focus:ring-2 focus:ring-[#5865f2] focus:ring-offset-2 focus:ring-offset-[#2c2f33]"
          aria-label="Accessibility"
        >
          <FontAwesomeIcon icon={faUniversalAccess} className="w-5 h-5" aria-hidden />
        </a>
        </Link>
      </header>

      <main className="flex-1 flex flex-col items-center justify-center px-6 py-12 sm:py-16">
        <motion.div
          className="text-center max-w-lg w-full"
          initial="initial"
          animate="animate"
          variants={{ animate: { transition: { staggerChildren: 0.08 } } }}
        >
          {/* Logo: PNG, fade + scale */}
          <motion.div
            className="flex justify-center mb-6"
            initial={{ opacity: 0, scale: 0.92 }}
            animate={{ opacity: 1, scale: 1 }}
            transition={{ duration: 0.5, ease: 'easeOut' }}
          >
            <img
              src="/fiskodo/logo_fiskodo.png"
              alt="Fiskodo"
              className="h-40 w-auto object-contain"
              width={320}
              height={160}
            />
          </motion.div>

          {/* Slogan */}
          <motion.p
            className="text-2xl sm:text-3xl font-bold text-white mb-3 tracking-tight"
            variants={fadeInUp}
          >
            no bullshit, just music
          </motion.p>

          {/* Short intro */}
          <motion.p
            className="text-[#b9bbbe] text-sm sm:text-base mb-10 max-w-md mx-auto leading-relaxed"
            variants={fadeInUp}
          >
            Add to your server, join a voice channel, and use the commands below.
          </motion.p>

          {/* Commands from MusicCommandModule */}
          <motion.div
            className="flex flex-wrap items-center justify-center gap-4 sm:gap-6 mb-10 text-[#72767d]"
            variants={fadeInUp}
          >
            <span className="flex flex-col items-center gap-1 max-w-[140px]" title="Play a single track or add it to the queue if something is already playing.">
              <code className="text-[#5865f2] font-mono text-sm">/play</code>
              <span className="text-xs text-center">Play a track or add to queue</span>
            </span>
            <span className="flex flex-col items-center gap-1 max-w-[140px]" title="Load a YouTube playlist or track(s). Use 'Add as next' to queue after current instead of replacing.">
              <code className="text-[#5865f2] font-mono text-sm">/playlist</code>
              <span className="text-xs text-center">Load playlist or track(s)</span>
            </span>
            <span className="flex flex-col items-center gap-1 max-w-[140px]" title="Skip to the next track.">
              <code className="text-[#5865f2] font-mono text-sm">/skip</code>
              <span className="text-xs text-center">Skip to next track</span>
            </span>
            <span className="flex flex-col items-center gap-1 max-w-[140px]" title="Go back to the previous track.">
              <code className="text-[#5865f2] font-mono text-sm">/previous</code>
              <span className="text-xs text-center">Go to previous track</span>
            </span>
            <span className="flex flex-col items-center gap-1 max-w-[140px]" title="Stop the current track and clear the queue.">
              <code className="text-[#5865f2] font-mono text-sm">/stop</code>
              <span className="text-xs text-center">Stop and clear queue</span>
            </span>
          </motion.div>

          {/* Invite CTA */}
          <motion.div variants={fadeInUp}>
            <motion.a
              href={BOT_INVITE_URL}
              target="_blank"
              rel="noopener noreferrer"
              aria-label="Invite Fiskodo bot to your Discord server"
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
              <FontAwesomeIcon icon={faDiscord} className="w-6 h-6 flex-shrink-0" aria-hidden />
              INVITE <i>Fiskodo</i>
            </motion.a>
          </motion.div>
        </motion.div>
      </main>

      <footer className="px-4 py-3 border-t border-[#202225]/50">
        <section id="a11y" className="text-center" />
      </footer>
    </div>
  )
}
