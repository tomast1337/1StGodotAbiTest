use glam::Vec2 as MathVec2;

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Vec2 {
    pub x: f32,
    pub y: f32,
}

impl From<Vec2> for MathVec2 {
    fn from(value: Vec2) -> Self {
        Self::new(value.x, value.y)
    }
}

impl From<MathVec2> for Vec2 {
    fn from(value: MathVec2) -> Self {
        Self {
            x: value.x,
            y: value.y,
        }
    }
}

#[repr(C)]
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Particle {
    pub pos: Vec2,
    pub vel: Vec2,
}

pub struct Simulation {
    particles: Vec<Particle>,
    sdf_grid: Vec<f32>,
    grid_width: usize,
    grid_height: usize,
    max_particles: usize,
    linear_damping: f32,
}

impl Simulation {
    fn try_new(max_particles: usize, width: usize, height: usize) -> Option<Self> {
        let grid_len = width.checked_mul(height)?;
        let mut particles = Vec::new();
        let mut sdf_grid = Vec::new();

        particles.try_reserve_exact(max_particles).ok()?;
        sdf_grid.try_reserve_exact(grid_len).ok()?;
        sdf_grid.resize(grid_len, 1.0);

        Some(Self {
            particles,
            sdf_grid,
            grid_width: width,
            grid_height: height,
            max_particles,
            linear_damping: 0.35,
        })
    }

    fn add_particle(&mut self, particle: Particle) -> bool {
        if self.particles.len() >= self.max_particles {
            return false;
        }

        self.particles.push(particle);
        true
    }

    fn update(&mut self, dt: f32) {
        if !dt.is_finite() || self.grid_width == 0 || self.grid_height == 0 {
            return;
        }

        let grid_width = self.grid_width;
        let grid_height = self.grid_height;
        let sdf_grid = &self.sdf_grid;

        for particle in &mut self.particles {
            let mut pos = MathVec2::from(particle.pos);
            let mut vel = MathVec2::from(particle.vel);

            pos += vel * dt;

            if let Some(distance) = sample_sdf(sdf_grid, grid_width, grid_height, pos) {
                if distance < 0.0 {
                    let normal = estimate_sdf_normal(sdf_grid, grid_width, grid_height, pos);
                    let speed_into_surface = vel.dot(normal);

                    if speed_into_surface < 0.0 {
                        vel -= normal * (2.0 * speed_into_surface);
                    }

                    pos -= normal * distance;
                }
            }

            // Exponential damping keeps behavior stable across frame rates.
            let damping_factor = (-self.linear_damping * dt).exp();
            vel *= damping_factor;

            particle.pos = pos.into();
            particle.vel = vel.into();
        }
    }

    fn set_linear_damping(&mut self, damping: f32) {
        if damping.is_finite() && damping >= 0.0 {
            self.linear_damping = damping;
        }
    }

    fn apply_attractor(&mut self, target: Vec2, strength: f32, radius: f32, dt: f32) {
        if !strength.is_finite() || !radius.is_finite() || !dt.is_finite() || radius <= 0.0 {
            return;
        }

        let target = MathVec2::from(target);
        if !target.is_finite() {
            return;
        }

        let radius_squared = radius * radius;
        for particle in &mut self.particles {
            let pos = MathVec2::from(particle.pos);
            let to_target = target - pos;
            let distance_squared = to_target.length_squared();

            if distance_squared <= f32::EPSILON || distance_squared > radius_squared {
                continue;
            }

            let distance = distance_squared.sqrt();
            let falloff = 1.0 - (distance / radius);
            let impulse = to_target / distance * strength * falloff * dt;
            let vel = MathVec2::from(particle.vel) + impulse;
            particle.vel = vel.into();
        }
    }
}

fn sample_sdf(sdf_grid: &[f32], width: usize, height: usize, pos: MathVec2) -> Option<f32> {
    if width == 0 || height == 0 || !pos.is_finite() {
        return None;
    }

    let x0 = pos.x.floor() as isize;
    let y0 = pos.y.floor() as isize;
    let x1 = x0 + 1;
    let y1 = y0 + 1;

    let tx = pos.x - pos.x.floor();
    let ty = pos.y - pos.y.floor();

    let get_val = |x: isize, y: isize| -> f32 {
        let cx = x.clamp(0, width as isize - 1) as usize;
        let cy = y.clamp(0, height as isize - 1) as usize;
        sdf_grid[cy * width + cx]
    };

    let c00 = get_val(x0, y0);
    let c10 = get_val(x1, y0);
    let c01 = get_val(x0, y1);
    let c11 = get_val(x1, y1);

    let top = c00 * (1.0 - tx) + c10 * tx;
    let bottom = c01 * (1.0 - tx) + c11 * tx;

    Some(top * (1.0 - ty) + bottom * ty)
}

fn estimate_sdf_normal(sdf_grid: &[f32], width: usize, height: usize, pos: MathVec2) -> MathVec2 {
    let eps = 0.1; 
    let right = sample_sdf(sdf_grid, width, height, pos + MathVec2::new(eps, 0.0)).unwrap_or(0.0);
    let left  = sample_sdf(sdf_grid, width, height, pos - MathVec2::new(eps, 0.0)).unwrap_or(0.0);
    let down  = sample_sdf(sdf_grid, width, height, pos + MathVec2::new(0.0, eps)).unwrap_or(0.0);
    let up    = sample_sdf(sdf_grid, width, height, pos - MathVec2::new(0.0, eps)).unwrap_or(0.0);

    MathVec2::new(right - left, down - up).normalize_or_zero()
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_create(max_particles: usize, width: usize, height: usize) -> *mut Simulation {
    let Some(sim) = Simulation::try_new(max_particles, width, height) else {
        return std::ptr::null_mut();
    };

    Box::into_raw(Box::new(sim))
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_destroy(sim_ptr: *mut Simulation) {
    if sim_ptr.is_null() {
        return;
    }

    unsafe {
        let _ = Box::from_raw(sim_ptr);
    }
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_step(sim_ptr: *mut Simulation, dt: f32) {
    let Some(sim) = (unsafe { sim_ptr.as_mut() }) else {
        return;
    };

    sim.update(dt);
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_clear_particles(sim_ptr: *mut Simulation) {
    let Some(sim) = (unsafe { sim_ptr.as_mut() }) else {
        return;
    };

    sim.particles.clear();
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_add_particle(sim_ptr: *mut Simulation, particle: Particle) -> u8 {
    let Some(sim) = (unsafe { sim_ptr.as_mut() }) else {
        return 0;
    };

    sim.add_particle(particle) as u8
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_get_particles(
    sim_ptr: *const Simulation,
    out_count: *mut usize,
) -> *const Particle {
    if !out_count.is_null() {
        unsafe {
            *out_count = 0;
        }
    }

    let Some(sim) = (unsafe { sim_ptr.as_ref() }) else {
        return std::ptr::null();
    };

    if !out_count.is_null() {
        unsafe {
            *out_count = sim.particles.len();
        }
    }

    sim.particles.as_ptr()
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_update_sdf(
    sim_ptr: *mut Simulation,
    sdf_data: *const f32,
    data_len: usize,
) -> u8 {
    let Some(sim) = (unsafe { sim_ptr.as_mut() }) else {
        return 0;
    };

    if sdf_data.is_null() || data_len != sim.sdf_grid.len() {
        return 0;
    }

    let host_slice = unsafe { std::slice::from_raw_parts(sdf_data, data_len) };
    sim.sdf_grid.copy_from_slice(host_slice);
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_apply_attractor(
    sim_ptr: *mut Simulation,
    target: Vec2,
    strength: f32,
    radius: f32,
    dt: f32,
) -> u8 {
    let Some(sim) = (unsafe { sim_ptr.as_mut() }) else {
        return 0;
    };

    sim.apply_attractor(target, strength, radius, dt);
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_set_linear_damping(sim_ptr: *mut Simulation, damping: f32) -> u8 {
    let Some(sim) = (unsafe { sim_ptr.as_mut() }) else {
        return 0;
    };

    sim.set_linear_damping(damping);
    1
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_particle_count(sim_ptr: *const Simulation) -> usize {
    let Some(sim) = (unsafe { sim_ptr.as_ref() }) else {
        return 0;
    };

    sim.particles.len()
}

#[unsafe(no_mangle)]
pub extern "C" fn sim_particle_capacity(sim_ptr: *const Simulation) -> usize {
    let Some(sim) = (unsafe { sim_ptr.as_ref() }) else {
        return 0;
    };

    sim.max_particles
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn respects_particle_capacity() {
        let mut sim = Simulation::try_new(1, 4, 4).unwrap();

        assert!(sim.add_particle(Particle::default()));
        assert!(!sim.add_particle(Particle::default()));
        assert_eq!(sim.particles.len(), 1);
    }

    #[test]
    fn moves_particles_by_velocity_and_dt() {
        let mut sim = Simulation::try_new(1, 4, 4).unwrap();
        sim.add_particle(Particle {
            pos: Vec2 { x: 1.0, y: 1.0 },
            vel: Vec2 { x: 2.0, y: -1.0 },
        });

        sim.update(0.5);

        assert_eq!(sim.particles[0].pos, Vec2 { x: 2.0, y: 0.5 });
    }

    #[test]
    fn rejects_sdf_with_wrong_length() {
        let sim = sim_create(1, 2, 2);
        let sdf = [1.0, 1.0, 1.0];

        assert_eq!(sim_update_sdf(sim, sdf.as_ptr(), sdf.len()), 0);

        sim_destroy(sim);
    }

    #[test]
    fn attractor_changes_velocity_toward_target() {
        let mut sim = Simulation::try_new(1, 4, 4).unwrap();
        sim.add_particle(Particle {
            pos: Vec2 { x: 0.0, y: 0.0 },
            vel: Vec2 { x: 0.0, y: 0.0 },
        });

        sim.apply_attractor(Vec2 { x: 2.0, y: 0.0 }, 10.0, 4.0, 1.0);

        assert!(sim.particles[0].vel.x > 0.0);
        assert_eq!(sim.particles[0].vel.y, 0.0);
    }
}
