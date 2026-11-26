using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    // A back and forth comes first: on when they speak within a minute of her reply, over after three quiet minutes.
    // Meanwhile her own initiatives wait (reminders and timers don't), and each restarts its own wait after, so nothing
    // comes in a rush.
    internal sealed partial class Companion
    {
        internal const double ConversationGap = 60, ConversationEnds = 180;

        private bool conversing;
        // When their last message came, when her last reply ended, and the last time either of them spoke.
        private double theirsAt = -1, herReplyEndedAt = -1, conversationLastAt = -1;
        // Their message is waiting on her reply, and whether her reply has started showing yet.
        private bool replyAwaited, replyShowing;

        internal bool Conversing { get { return conversing; } }

        // A message from them, typed or said.
        private void ConversationHeard(double now)
        {
            if (!conversing && herReplyEndedAt >= 0 && now - herReplyEndedAt <= ConversationGap)
            {
                conversing = true;
                Brain.Log("conversation", -1, "A back and forth: her own lines wait until it ends.");
            }
            theirsAt = now;
            conversationLastAt = now;
            replyAwaited = true;
            replyShowing = false;
        }

        // On her tick: her reply showing and then ending, and the conversation ending after its quiet stretch.
        private void ConversationTick(double now, bool talking)
        {
            if (replyAwaited)
            {
                if (talking) replyShowing = true;
                else if (replyShowing)
                {
                    replyAwaited = replyShowing = false;
                    herReplyEndedAt = now;
                    conversationLastAt = now;
                }
                // No reply at all in two minutes: nothing to wait on.
                else if (now - theirsAt > 120) replyAwaited = false;
            }
            if (talking) conversationLastAt = now;
            if (conversing && !replyAwaited && now - conversationLastAt >= ConversationEnds) ConversationOver(now);
        }

        private bool HerTalking { get { return speech != null || talkState != TalkState.Off; } }

        // Each held kind starts its own wait from now, so nothing held comes out in a rush.
        private void ConversationOver(double now)
        {
            conversing = false;
            try
            {
                if (care != null) care.Reset();
                if (nextPopIn >= 0) nextPopIn = now + PopGap();
                if (nextGlance >= 0) nextGlance = now + GlanceGap;
                if (noticed >= 0) noticed = now;
            }
            catch (Exception caughtOne) { CapriDesktop.Brain.Caught(caughtOne); }
            Brain.Log("conversation", -1, "The back and forth ended: her own lines start their waits again.");
        }

        // While it is on, the timed ones keep their next chance a whole wait away.
        private void ConversationHolds(double now)
        {
            if (!conversing) return;
            if (nextPopIn >= 0) nextPopIn = Math.Max(nextPopIn, now + PopGap() / 2);
            if (nextGlance >= 0) nextGlance = Math.Max(nextGlance, now + GlanceGap);
        }

        // Driven, not waited for: a back and forth starts and ends by the gaps between turns, what she does off her own bat waits
        // while it is on, a pat still lands, and when it ends each held kind starts its own wait from then.
#if CHECKS
        private void SmokeConversation(List<string> checks)
        {
            bool wasOn = conversing, wasAwaited = replyAwaited, wasShowing = replyShowing;
            double wasTheirs = theirsAt, wasEnded = herReplyEndedAt, wasLast = conversationLastAt, wasPop = nextPopIn, wasGlance = nextGlance;
            try
            {
                conversing = false;
                herReplyEndedAt = -1;
                double t = 100000;
                ConversationHeard(t);
                if (conversing) throw new Exception("A first message started a conversation on its own.");
                ConversationTick(t + 2, true);
                ConversationTick(t + 8, false);
                if (herReplyEndedAt != t + 8) throw new Exception("Her reply's end wasn't seen.");
                ConversationHeard(t + 8 + 70);
                if (conversing) throw new Exception("A message 70 s after her reply started a conversation.");
                ConversationTick(t + 80, true);
                ConversationTick(t + 85, false);
                ConversationHeard(t + 85 + 40);
                if (!conversing) throw new Exception("A message 40 s after her reply didn't start a conversation.");

                // What she does off her own bat waits; the pop-in and screen timers stay a whole wait away.
                nextPopIn = t + 130;
                nextGlance = t + 130;
                ConversationHolds(t + 126);
                if (nextPopIn <= t + 130 || nextGlance < t + 126 + GlanceGap - 1) throw new Exception("A timed line wasn't held off while they talked.");
                if (PopFree(t + 126, 0)) throw new Exception("She was free to pop in mid conversation.");

                // Still on while her reply shows and less than three quiet minutes pass; off after.
                ConversationTick(t + 126, true);
                ConversationTick(t + 130, false);
                ConversationTick(t + 130 + 170, false);
                if (!conversing) throw new Exception("The conversation ended before three quiet minutes.");
                ConversationTick(t + 130 + 181, false);
                if (conversing) throw new Exception("The conversation didn't end after three quiet minutes.");
                if (nextPopIn < t + 130 + 181 + PopMin * 60 - 1 || nextGlance < t + 130 + 181 + GlanceGap - 1) throw new Exception("A held line's wait didn't start again from the end.");
                checks.Add("PASS a back and forth comes first: a message within a minute of her reply starts it and one after 70 s doesn't; while it is on she isn't free to pop in and her timed lines stay a whole wait away; it ends after three quiet minutes, and each held kind starts its wait again from the end");
            }
            finally
            {
                conversing = wasOn; replyAwaited = wasAwaited; replyShowing = wasShowing;
                theirsAt = wasTheirs; herReplyEndedAt = wasEnded; conversationLastAt = wasLast; nextPopIn = wasPop; nextGlance = wasGlance;
            }
        }
#endif
    }
}
