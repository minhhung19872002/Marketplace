# The security baseline every ShopHub response carries (spec 6.1). Included at server level AND inside every
# location that sets an add_header of its own — nginx drops inherited add_header directives in such a location
# (NginxConfigParityTests checks both).
add_header X-Content-Type-Options "nosniff" always;
# Ignored by browsers over plain HTTP (dev); effective behind HTTPS (docker-compose.prod.yml)
add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;
add_header X-Frame-Options "SAMEORIGIN" always;
add_header Referrer-Policy "strict-origin-when-cross-origin" always;
add_header Content-Security-Policy "default-src 'self'; script-src 'self' https://accounts.google.com/gsi/client https://sp.zalo.me; frame-src https://accounts.google.com/gsi/ https://*.zalo.me; img-src 'self' data: https:; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://accounts.google.com/gsi/style; font-src 'self' https://fonts.gstatic.com; connect-src 'self' ws: wss: https://accounts.google.com/gsi/" always;
