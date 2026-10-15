// Frozen pre-reuse production draw method for paired fixture comparison.
window.__sceneStaticBaseline = function (content, preview = null) {
    const { width, height } = this.resize(),
        ctx = this.context;
    ctx.clearRect(0, 0, width, height);
    const sky = ctx.createRadialGradient(
        width * 0.5,
        height * 0.4,
        30,
        width * 0.5,
        height * 0.5,
        Math.max(width, height),
    );
    sky.addColorStop(0, "#203742");
    sky.addColorStop(0.55, "#0c1b27");
    sky.addColorStop(1, "#040b13");
    ctx.fillStyle = sky;
    ctx.fillRect(0, 0, width, height);
    this.drawGrid(width, height);
    this.visibleBounds = {
        left: this.camera.x - width / (this.camera.zoom * 2),
        right: this.camera.x + width / (this.camera.zoom * 2),
        top: this.camera.y - height / (this.camera.zoom * 2),
        bottom: this.camera.y + height / (this.camera.zoom * 2),
    };
    ctx.save();
    ctx.translate(width / 2, height / 2);
    ctx.scale(this.camera.zoom, this.camera.zoom);
    ctx.translate(-this.camera.x, -this.camera.y);
    for (const image of content.images || []) {
        const loaded = this.images.get(image.path)?.image;
        const bounds = this.visibleBounds;
        if (
            image.position.x + image.width < bounds.left ||
            image.position.x > bounds.right ||
            image.position.y + image.height < bounds.top ||
            image.position.y > bounds.bottom
        )
            continue;
        if (loaded?.complete && loaded.naturalWidth)
            ctx.drawImage(loaded, image.position.x, image.position.y, image.width, image.height);
    }
    for (const stroke of content.strokes || []) if (this.visible(stroke, width, height)) this.drawStroke(stroke);
    if (preview) this.drawStroke(preview);
    ctx.restore();
};
