use crate::person::Person;

pub struct World {
    people: Vec<Box<dyn Person>>,
}

impl World {
    pub fn new() -> Self {
        Self { people: Vec::new() }
    }

    pub fn add(&mut self, person: Box<dyn Person>) {
        self.people.push(person);
    }

    pub fn hello_all(&self) -> Vec<String> {
        self.people.iter().map(|p| p.hello()).collect()
    }
}
