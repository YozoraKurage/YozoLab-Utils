using YozoLab.SPS.Ndmf;
using System.Collections.Generic;
using UnityEngine;
using YozoLab.SPS.Injector;
using YozoLab.SPS.Utils;
using YozoLab.SPS.Utils.Controller;

namespace YozoLab.SPS.Service {
    /**
     * Keeps tabs on all clips in the entire build. Not the same as all clips in the controllers,
     * since sometimes clips are stored separately temporarily, and then used later.
     */
    [VFService]
    internal class AllClipsService {
        [VFAutowired] private readonly ControllersService controllers;
        [VFAutowired] private readonly ExistingClipsService existingClips;
        private readonly List<VFClip> additionalClips = new List<VFClip>();

        public void RewriteAllClips(AnimationRewriter rewriter) {
            foreach (var c in controllers.GetAllUsedControllers()) {
                c.Rewrite(rewriter);
            }
            foreach (var clip in additionalClips) {
                clip.Rewrite(rewriter);
            }
            foreach (var clip in existingClips.GetClips()) {
                clip.Rewrite(rewriter);
            }
        }

        /**
         * Note: Does not update audio clip source paths
         */
        public ISet<VFClip> GetAllClips() {
            var clips = new HashSet<VFClip>();
            foreach (var c in controllers.GetAllUsedControllers()) {
                clips.UnionWith(c.GetClips());
            }
            clips.UnionWith(additionalClips);
            clips.UnionWith(existingClips.GetClips());
            return clips;
        }

        public void AddAdditionalManagedClip(VFClip clip) {
            if (clip == null) return;
            additionalClips.Add(clip);
        }
    }
}
