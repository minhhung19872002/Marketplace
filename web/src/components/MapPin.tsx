import { useEffect, useRef } from 'react';
import L from 'leaflet';
import 'leaflet/dist/leaflet.css';
import './MapPin.css';

interface Props {
  lat: number | null;
  lng: number | null;
  onPick: (lat: number, lng: number) => void;
}

// Whole of Vietnam until a point is pinned
const VIETNAM: L.LatLngTuple = [16.05, 106.5];
const round = (n: number) => Math.round(n * 1e6) / 1e6;

/**
 * "Ghim bản đồ" for an address (I.2, E7): OpenStreetMap tiles through Leaflet, no key. Click (or tap) the map to drop the
 * pin; the pin is a circle marker so no image asset is needed.
 */
const MapPin = ({ lat, lng, onPick }: Props) => {
  const box = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null);
  const pin = useRef<L.CircleMarker | null>(null);
  const pick = useRef(onPick);
  pick.current = onPick;

  useEffect(() => {
    if (!box.current) return;
    const m = L.map(box.current, { center: lat != null && lng != null ? [lat, lng] : VIETNAM, zoom: lat != null ? 16 : 5 });
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap</a>',
    }).addTo(m);
    m.on('click', (e: L.LeafletMouseEvent) => pick.current(round(e.latlng.lat), round(e.latlng.lng)));
    map.current = m;
    return () => {
      m.remove();
      map.current = null;
      pin.current = null;
    };
    // The map is created once; later pins move the marker below
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    const m = map.current;
    if (!m) return;
    if (lat == null || lng == null) {
      pin.current?.remove();
      pin.current = null;
      return;
    }
    if (pin.current) pin.current.setLatLng([lat, lng]);
    else pin.current = L.circleMarker([lat, lng], { radius: 9, weight: 3, className: 'map-pin-marker' }).addTo(m);
  }, [lat, lng]);

  return <div ref={box} className="map-pin" role="application" aria-label="Bản đồ — bấm để ghim vị trí" data-testid="address-map" />;
};

export default MapPin;
